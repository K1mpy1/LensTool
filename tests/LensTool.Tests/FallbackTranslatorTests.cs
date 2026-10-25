using System.IO;
using LensTool.Translation;

namespace LensTool.Tests;

public class FallbackTranslatorTests : IDisposable
{
    private readonly string _usageFile = Path.Combine(Path.GetTempPath(), $"lenstool-usage-{Guid.NewGuid():N}.json");

    public void Dispose() => File.Delete(_usageFile);

    [Fact]
    public async Task FirstEngineIsUsedAndCounted()
    {
        var google = new StubTranslator("Google");
        var deepl = new StubTranslator("DeepL");
        var usage = new UsageTracker(_usageFile);

        var result = await Chain(usage, google, deepl).TranslateAsync("hello", "RU");

        Assert.Equal("Google", result.Engine);
        Assert.Equal(1, google.Calls);
        Assert.Equal(0, deepl.Calls);
        Assert.Equal(5, usage.Used("Google"));
        Assert.Equal(0, usage.Used("DeepL"));
    }

    [Fact]
    public async Task UnconfiguredOrFailingEngineFallsThrough()
    {
        var google = new StubTranslator("Google", new TranslatorUnavailableException("нет ключа Google"));
        var deepl = new StubTranslator("DeepL");
        var usage = new UsageTracker(_usageFile);

        var result = await Chain(usage, google, deepl).TranslateAsync("hello", "RU");

        Assert.Equal("DeepL", result.Engine);
        Assert.Equal(0, usage.Used("Google")); // failed requests are not counted
        Assert.Equal(5, usage.Used("DeepL"));
    }

    [Fact]
    public async Task EngineNearLimitIsSkipped()
    {
        var google = new StubTranslator("Google");
        var deepl = new StubTranslator("DeepL");
        var usage = new UsageTracker(_usageFile);
        usage.Add("Google", 895); // budget = 1000 * 0.9 = 900

        var result = await Chain(usage, google, deepl).TranslateAsync("hello!", "RU");

        Assert.Equal("DeepL", result.Engine);
        Assert.Equal(0, google.Calls);
    }

    [Fact]
    public async Task AllEnginesFailingGivesCombinedMessage()
    {
        var chain = Chain(new UsageTracker(_usageFile),
            new StubTranslator("Google", new TranslatorUnavailableException("нет ключа Google")),
            new StubTranslator("DeepL", new TranslationException("лимит DeepL")));

        var ex = await Assert.ThrowsAsync<TranslationException>(() => chain.TranslateAsync("hello", "RU"));

        Assert.Contains("нет ключа Google", ex.Message);
        Assert.Contains("лимит DeepL", ex.Message);
    }

    private static FallbackTranslator Chain(UsageTracker usage, params ITranslator[] engines) =>
        new(engines, usage, monthlyFreeCharacters: 1000, switchAtShare: 0.9);

    private sealed class StubTranslator(string name, Exception? error = null) : ITranslator
    {
        public int Calls { get; private set; }

        public string Name => name;

        public Task<TranslationResult> TranslateAsync(string text, string targetLanguage, string? sourceLanguage = null,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return error is null
                ? Task.FromResult(new TranslationResult($"[{name}] {text}", "EN", targetLanguage))
                : Task.FromException<TranslationResult>(error);
        }
    }
}
