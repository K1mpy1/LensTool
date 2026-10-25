namespace LensTool.Translation.Offline;

/// <summary>A file of a model: path in the Hugging Face repo, size and SHA-256 for verification.</summary>
public sealed record ModelFile(string RepoPath, long Size, string Sha256)
{
    public string FileName => RepoPath.Split('/')[^1];
}

/// <summary>
/// An Opus-MT (Helsinki-NLP, CC-BY 4.0) translation model in ONNX form, pinned to a repo revision.
/// Only int8-quantized encoder/decoder are used: ~4x smaller than fp32 with near-identical quality.
/// </summary>
public sealed record OpusModel(string Source, string Target, string Repo, string Revision, IReadOnlyList<ModelFile> Files)
{
    public string Id => $"opus-mt-{Source}-{Target}";

    public long TotalSize => Files.Sum(f => f.Size);

    public Uri DownloadUri(ModelFile file) => new($"https://huggingface.co/{Repo}/resolve/{Revision}/{file.RepoPath}");

    public static OpusModel EnglishToRussian { get; } = new("en", "ru", "Xenova/opus-mt-en-ru", "e050376e960175e8ddc5cff85025dc8436cddd68",
    [
        new("config.json", 1376, "db16bf0ea7bac239273214c3b9ab44577884227371a9a25e0191b7baf8d6f167"),
        new("source.spm", 802781, "16bebef1389a0b8ab452772c4e35b9e605e5713f8ac7baa71ca701394eaa086d"),
        new("target.spm", 1080169, "745998e51ba5b058e38b7ac7765c25c43ed5c1c39cc92b27163b9b2e323c9d7c"),
        new("vocab.json", 2726796, "5cf0d95d930d8d3e783c9e2f46a72f08b43a18060dab4ddefbcb66a733efedcb"),
        new("onnx/encoder_model_quantized.onnx", 51628446, "97f28d8295d231b7da721ded06fa8c034787df2122cd8f85b904abdc4d15bd53"),
        new("onnx/decoder_model_quantized.onnx", 58560874, "7e4e602c31130176b086cd4bf31d6d08d44a6a367617e910870e0ea3e5408574"),
    ]);

    public static OpusModel RussianToEnglish { get; } = new("ru", "en", "Xenova/opus-mt-ru-en", "afe8c6c738ec81b6d033fd8f44f9678a639a7c67",
    [
        new("config.json", 1376, "60dc7d0906dbe42a1d740cdd7e84ce1534685b81fbe16005a0301adac69086ce"),
        new("source.spm", 1080169, "745998e51ba5b058e38b7ac7765c25c43ed5c1c39cc92b27163b9b2e323c9d7c"),
        new("target.spm", 802781, "16bebef1389a0b8ab452772c4e35b9e605e5713f8ac7baa71ca701394eaa086d"),
        new("vocab.json", 2726796, "5cf0d95d930d8d3e783c9e2f46a72f08b43a18060dab4ddefbcb66a733efedcb"),
        new("onnx/encoder_model_quantized.onnx", 51628446, "c10df0466540534bb70c4402d539c349b8bfa9968184cad89f51408cf13e7eb7"),
        new("onnx/decoder_model_quantized.onnx", 58560874, "d880abf58612013815554ef55c98d0f28b68190b2cb6f65c39d6e834f51dda8c"),
    ]);

    public static IReadOnlyList<OpusModel> All { get; } = [EnglishToRussian, RussianToEnglish];
}
