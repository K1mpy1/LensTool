namespace LensTool.Translation;

/// <summary>
/// Tries engines in order. An engine is skipped when it is not configured, fails,
/// or would exceed its share of the monthly free character limit.
/// </summary>
public sealed class FallbackTranslator(
    IReadOnlyList<ITranslator> engines, UsageTracker usage, long monthlyFreeCharacters, double switchAtShare) : ITranslator
{
    public string Name => string.Join(" → ", engines.Select(e => e.Name));

    /// <summary>Characters per engine per month after which the next engine is used.</summary>
    public long Budget => (long)(monthlyFreeCharacters * switchAtShare);

    public async Task<TranslationResult> TranslateAsync(string text, string targetLanguage, string? sourceLanguage = null,
        CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        foreach (var engine in engines)
        {
            long used = usage.Used(engine.Name);
            if (engine.IsMetered && used + text.Length > Budget)
            {
                AppLog.Info($"{engine.Name}: израсходовано {used} из {monthlyFreeCharacters} символов за месяц, пропускаю");
                errors.Add($"{engine.Name}: месячный бесплатный лимит почти исчерпан ({used:N0} из {monthlyFreeCharacters:N0}).");
                continue;
            }

            try
            {
                var result = await engine.TranslateAsync(text, targetLanguage, sourceLanguage, cancellationToken);
                usage.Add(engine.Name, text.Length);
                return result with { Engine = engine.Name };
            }
            catch (TranslationException ex) when (!cancellationToken.IsCancellationRequested)
            {
                AppLog.Info($"{engine.Name} не перевёл: {ex.Message}");
                errors.Add(ex.Message);
            }
        }

        throw new TranslationException(string.Join(Environment.NewLine, errors));
    }
}
