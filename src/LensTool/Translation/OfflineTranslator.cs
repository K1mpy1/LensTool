using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using LensTool.Translation.Offline;

namespace LensTool.Translation;

/// <summary>
/// Offline translation with Opus-MT models (English ↔ Russian). No key, no limits, text stays on this PC.
/// The model for a direction is downloaded on first use and kept loaded afterwards.
/// </summary>
public sealed partial class OfflineTranslator(ModelStore store) : ITranslator, IDisposable
{
    private readonly Dictionary<string, Task<MarianModel>> _models = [];
    private readonly object _sync = new();

    public string Name => "Offline";

    public bool IsMetered => false;

    public async Task<TranslationResult> TranslateAsync(string text, string targetLanguage, string? sourceLanguage = null,
        CancellationToken cancellationToken = default)
    {
        string target = BaseLanguage(targetLanguage);
        string source = sourceLanguage is null ? DetectLanguage(text) : BaseLanguage(sourceLanguage);

        // Already in the target language: nothing to do (TranslationService then asks for the fallback target).
        if (source == target)
            return new TranslationResult(text, source.ToUpperInvariant(), targetLanguage.ToUpperInvariant());

        var definition = OpusModel.All.FirstOrDefault(m => m.Source == source && m.Target == target)
            ?? throw new TranslatorUnavailableException(
                $"Офлайн-перевод поддерживает только английский ↔ русский, а не {source.ToUpperInvariant()} → {target.ToUpperInvariant()}.");

        MarianModel model;
        try
        {
            model = await LoadAsync(definition, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new TranslationException($"Не удалось подготовить офлайн-модель {definition.Id}: {ex.Message}", ex);
        }

        var sw = Stopwatch.StartNew();
        string translated = await Task.Run(() => TranslateParagraphs(model, text, cancellationToken), cancellationToken);
        AppLog.Info($"Offline {definition.Id}: {text.Length} символов, {sw.ElapsedMilliseconds} мс");
        return new TranslationResult(translated, source.ToUpperInvariant(), targetLanguage.ToUpperInvariant());
    }

    /// <summary>"RU" or "EN" by the dominant script: Cyrillic letters mean Russian, anything else English.</summary>
    public static string DetectLanguage(string text)
    {
        int cyrillic = 0, latin = 0;
        foreach (char c in text)
        {
            if (c is >= 'Ѐ' and <= 'ӿ')
                cyrillic++;
            else if (char.IsAsciiLetter(c))
                latin++;
        }
        return cyrillic > latin ? "ru" : "en";
    }

    /// <summary>Splits text into paragraphs and sentences: Marian translates sentence-sized input best.</summary>
    public static IReadOnlyList<IReadOnlyList<string>> SplitSentences(string text) =>
        text.Split('\n')
            .Select(p => (IReadOnlyList<string>)SentenceBoundary().Split(p.Trim()).Where(s => s.Length > 0).ToList())
            .ToList();

    private static string TranslateParagraphs(MarianModel model, string text, CancellationToken ct)
    {
        var sb = new StringBuilder();
        foreach (var paragraph in SplitSentences(text))
        {
            if (sb.Length > 0)
                sb.Append('\n');
            sb.AppendJoin(' ', paragraph.Select(sentence => HasLetters(sentence) ? model.Translate(sentence, ct) : sentence));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Shared load of a model. The download is not tied to the caller's token: closing the popup only stops
    /// waiting, the model keeps downloading, and another caller never gets someone else's cancellation.
    /// </summary>
    private Task<MarianModel> LoadAsync(OpusModel definition, CancellationToken ct)
    {
        lock (_sync)
        {
            if (!_models.TryGetValue(definition.Id, out var task) || task.IsFaulted || task.IsCanceled)
            {
                task = Task.Run(async () =>
                {
                    string folder = await store.EnsureAsync(definition);
                    var sw = Stopwatch.StartNew();
                    var model = new MarianModel(folder);
                    AppLog.Info($"Модель {definition.Id} загружена за {sw.ElapsedMilliseconds} мс");
                    return model;
                });
                _models[definition.Id] = task;
            }
            return task.WaitAsync(ct);
        }
    }

    private static bool HasLetters(string s) => s.Any(char.IsLetter);

    private static string BaseLanguage(string code) => code.Split('-')[0].ToLowerInvariant();

    // A sentence ends with . ! ? or … followed by whitespace and an uppercase letter, digit or opening quote.
    [GeneratedRegex(@"(?<=[.!?…])\s+(?=[\p{Lu}\d""«(])")]
    private static partial Regex SentenceBoundary();

    public void Dispose()
    {
        lock (_sync)
        {
            foreach (var task in _models.Values)
                if (task.IsCompletedSuccessfully)
                    task.Result.Dispose();
            _models.Clear();
        }
    }
}
