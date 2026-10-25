using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace LensTool.Translation.Offline;

/// <summary>
/// Runs a Marian (Opus-MT) encoder-decoder exported to ONNX: SentencePiece pieces are mapped through
/// vocab.json, the encoder runs once, then the decoder is run step by step with beam search.
/// The decoder has no KV cache, so each step re-reads the whole prefix; fine for sentence-sized input.
/// </summary>
public sealed class MarianModel : IDisposable
{
    private const int BeamSize = 4;
    private const int MaxSourceTokens = 256;

    private readonly InferenceSession _encoder;
    private readonly InferenceSession _decoder;
    private readonly SentencePieceTokenizer _sourceTokenizer;
    private readonly Dictionary<string, int> _vocab;
    private readonly string[] _pieces;
    private readonly int _unkId, _eosId, _padId, _decoderStartId;

    public MarianModel(string folder)
    {
        using (var spm = File.OpenRead(Path.Combine(folder, "source.spm")))
            _sourceTokenizer = SentencePieceTokenizer.Create(spm, addBeginningOfSentence: false, addEndOfSentence: false);

        _vocab = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(Path.Combine(folder, "vocab.json")))
                 ?? throw new InvalidDataException("vocab.json пуст.");
        _pieces = new string[_vocab.Values.Max() + 1];
        foreach (var (piece, id) in _vocab)
            _pieces[id] = piece;

        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "config.json")));
        _eosId = config.RootElement.GetProperty("eos_token_id").GetInt32();
        _padId = config.RootElement.GetProperty("pad_token_id").GetInt32();
        _decoderStartId = config.RootElement.GetProperty("decoder_start_token_id").GetInt32();
        _unkId = _vocab.GetValueOrDefault("<unk>", 1);

        var options = new SessionOptions { GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL };
        _encoder = new InferenceSession(Path.Combine(folder, "encoder_model_quantized.onnx"), options);
        _decoder = new InferenceSession(Path.Combine(folder, "decoder_model_quantized.onnx"), options);
    }

    /// <summary>Translates one sentence (or a short paragraph).</summary>
    public string Translate(string text, CancellationToken cancellationToken = default)
    {
        long[] source = Encode(text);
        int n = source.Length;
        var mask = Enumerable.Repeat(1L, n).ToArray();

        float[] encoderStates;
        int hidden;
        using (var encoded = _encoder.Run(
        [
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(source, [1, n])),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(mask, [1, n])),
        ]))
        {
            var states = encoded.First().AsTensor<float>();
            hidden = states.Dimensions[2];
            encoderStates = Flatten(states);
        }

        var ids = BeamSearch(encoderStates, n, hidden, maxLength: Math.Min(512, 2 * n + 16), cancellationToken);
        return Decode(ids);
    }

    private long[] Encode(string text)
    {
        var tokens = _sourceTokenizer.EncodeToTokens(text, out _, addBeginningOfSentence: false, addEndOfSentence: false);
        var ids = tokens.Take(MaxSourceTokens - 1).Select(t => (long)_vocab.GetValueOrDefault(t.Value, _unkId)).ToList();
        ids.Add(_eosId);
        return [.. ids];
    }

    private string Decode(IEnumerable<int> ids)
    {
        var sb = new StringBuilder();
        foreach (int id in ids)
        {
            if (id == _eosId || id == _padId || id == _decoderStartId || id >= _pieces.Length)
                continue;
            sb.Append(id == _unkId ? "?" : _pieces[id]);
        }
        // SentencePiece marks word starts with U+2581.
        return sb.ToString().Replace('▁', ' ').Trim();
    }

    private List<int> BeamSearch(float[] encoderStates, int sourceLength, int hidden, int maxLength, CancellationToken ct)
    {
        var alive = new List<(List<int> Ids, double Score)> { ([_decoderStartId], 0) };
        var finished = new List<(List<int> Ids, double Score)>();

        for (int step = 0; step < maxLength && alive.Count > 0; step++)
        {
            ct.ThrowIfCancellationRequested();
            float[] logits = RunDecoder(alive.Select(b => b.Ids).ToList(), encoderStates, sourceLength, hidden, out int vocabSize);
            int t = alive[0].Ids.Count;

            var candidates = new List<(int Beam, int Token, double Score)>();
            for (int b = 0; b < alive.Count; b++)
            {
                var span = logits.AsSpan(((b * t) + t - 1) * vocabSize, vocabSize);
                span[_padId] = float.NegativeInfinity; // never generate padding (bad_words_ids)
                double logSum = LogSumExp(span);
                foreach (int token in TopK(span, 2 * BeamSize))
                    candidates.Add((b, token, alive[b].Score + span[token] - logSum));
            }

            var next = new List<(List<int> Ids, double Score)>();
            foreach (var c in candidates.OrderByDescending(c => c.Score))
            {
                var ids = new List<int>(alive[c.Beam].Ids) { c.Token };
                if (c.Token == _eosId)
                    finished.Add((ids, c.Score / ids.Count)); // length penalty 1.0, as in Hugging Face
                else
                    next.Add((ids, c.Score));
                if (next.Count == BeamSize)
                    break;
            }
            alive = next;

            if (finished.Count >= BeamSize)
                break;
        }

        var best = finished.Count > 0
            ? finished.MaxBy(f => f.Score)
            : alive.MaxBy(a => a.Score / a.Ids.Count);
        return best.Ids;
    }

    /// <summary>Runs the decoder on a batch of equal-length prefixes; returns logits [batch, t, vocab] flattened.</summary>
    private float[] RunDecoder(List<List<int>> prefixes, float[] encoderStates, int sourceLength, int hidden, out int vocabSize)
    {
        int batch = prefixes.Count, t = prefixes[0].Count;
        var inputIds = new long[batch * t];
        for (int b = 0; b < batch; b++)
            for (int i = 0; i < t; i++)
                inputIds[b * t + i] = prefixes[b][i];

        var states = new float[batch * sourceLength * hidden];
        var mask = new long[batch * sourceLength];
        for (int b = 0; b < batch; b++)
        {
            encoderStates.CopyTo(states, b * encoderStates.Length);
            Array.Fill(mask, 1L, b * sourceLength, sourceLength);
        }

        using var output = _decoder.Run(
        [
            NamedOnnxValue.CreateFromTensor("encoder_attention_mask", new DenseTensor<long>(mask, [batch, sourceLength])),
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(inputIds, [batch, t])),
            NamedOnnxValue.CreateFromTensor("encoder_hidden_states", new DenseTensor<float>(states, [batch, sourceLength, hidden])),
        ]);
        var logits = output.First(o => o.Name == "logits").AsTensor<float>();
        vocabSize = logits.Dimensions[2];
        return Flatten(logits);
    }

    /// <summary>Copies a tensor into a flat array (fast path for dense tensors).</summary>
    private static float[] Flatten(Tensor<float> tensor) =>
        tensor is DenseTensor<float> dense ? dense.Buffer.ToArray() : [.. tensor];

    private static double LogSumExp(ReadOnlySpan<float> values)
    {
        float max = float.NegativeInfinity;
        foreach (float v in values)
            if (v > max) max = v;
        double sum = 0;
        foreach (float v in values)
            sum += Math.Exp(v - max);
        return max + Math.Log(sum);
    }

    private static int[] TopK(ReadOnlySpan<float> values, int k)
    {
        var best = new List<int>(k + 1);
        for (int i = 0; i < values.Length; i++)
        {
            if (best.Count == k && values[i] <= values[best[^1]])
                continue;
            int pos = best.Count;
            while (pos > 0 && values[best[pos - 1]] < values[i])
                pos--;
            best.Insert(pos, i);
            if (best.Count > k)
                best.RemoveAt(k);
        }
        return [.. best];
    }

    public void Dispose()
    {
        _encoder.Dispose();
        _decoder.Dispose();
    }
}
