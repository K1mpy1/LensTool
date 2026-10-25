namespace LensTool.Translation;

/// <summary>
/// Picks the target language: text is translated into <c>targetLanguage</c>,
/// and text that is already in that language goes to <c>fallbackTargetLanguage</c> instead.
/// </summary>
public sealed class TranslationService(ITranslator translator, string targetLanguage, string fallbackTargetLanguage)
{
    public ITranslator Translator => translator;

    public async Task<TranslationResult> TranslateAsync(string text, CancellationToken cancellationToken = default)
    {
        var result = await translator.TranslateAsync(text, targetLanguage, cancellationToken: cancellationToken);
        if (!SameLanguage(result.DetectedSourceLanguage, targetLanguage))
            return result;

        // Second request only for text already in the target language; the source is known now.
        return await translator.TranslateAsync(text, fallbackTargetLanguage, result.DetectedSourceLanguage, cancellationToken);
    }

    /// <summary>Compares base languages: "EN" matches "EN-US", "pt-BR" matches "PT".</summary>
    public static bool SameLanguage(string a, string b) =>
        string.Equals(BaseLanguage(a), BaseLanguage(b), StringComparison.OrdinalIgnoreCase);

    private static string BaseLanguage(string code) => code.Split('-')[0];
}
