namespace LensTool.Translation;

/// <summary>Text translation. Implementations are swappable (DeepL, Google, local models…).</summary>
public interface ITranslator
{
    /// <summary>Engine name for logs and settings.</summary>
    string Name { get; }

    /// <summary>The engine has a monthly character quota; unmetered engines (offline) are never skipped for usage.</summary>
    bool IsMetered => true;

    /// <param name="targetLanguage">Target language code, e.g. "RU" or "EN-US".</param>
    /// <param name="sourceLanguage">Source language code; null = auto-detect.</param>
    Task<TranslationResult> TranslateAsync(string text, string targetLanguage, string? sourceLanguage = null,
        CancellationToken cancellationToken = default);
}

/// <param name="DetectedSourceLanguage">Source language as reported by the engine, upper case (e.g. "EN").</param>
/// <param name="Engine">Engine that produced the translation; set by <see cref="FallbackTranslator"/>.</param>
public sealed record TranslationResult(string Text, string DetectedSourceLanguage, string TargetLanguage, string? Engine = null);

/// <summary>Translation failed; <see cref="Exception.Message"/> is shown to the user.</summary>
public class TranslationException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>The engine is not configured (e.g. no API key).</summary>
public sealed class TranslatorUnavailableException(string message) : TranslationException(message);
