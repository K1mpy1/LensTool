using LensTool.Translation;

namespace LensTool.Tests;

public class TranslationServiceTests
{
    [Fact]
    public async Task ForeignTextGoesToTargetLanguageInOneRequest()
    {
        var fake = new FakeTranslator(detected: "EN");
        var service = new TranslationService(fake, "RU", "EN-US");

        var result = await service.TranslateAsync("Hello");

        Assert.Equal("RU", result.TargetLanguage);
        Assert.Equal([("RU", null)], fake.Calls);
    }

    [Fact]
    public async Task TextAlreadyInTargetLanguageGoesToFallback()
    {
        var fake = new FakeTranslator(detected: "RU");
        var service = new TranslationService(fake, "RU", "EN-US");

        var result = await service.TranslateAsync("Привет");

        Assert.Equal("EN-US", result.TargetLanguage);
        Assert.Equal([("RU", null), ("EN-US", "RU")], fake.Calls);
    }

    [Theory]
    [InlineData("EN", "EN-US", true)]
    [InlineData("pt-BR", "PT", true)]
    [InlineData("ru", "RU", true)]
    [InlineData("EN", "RU", false)]
    public void SameLanguageComparesBaseCodes(string a, string b, bool expected) =>
        Assert.Equal(expected, TranslationService.SameLanguage(a, b));

    private sealed class FakeTranslator(string detected) : ITranslator
    {
        public List<(string Target, string? Source)> Calls { get; } = [];

        public string Name => "Fake";

        public Task<TranslationResult> TranslateAsync(string text, string targetLanguage, string? sourceLanguage = null,
            CancellationToken cancellationToken = default)
        {
            Calls.Add((targetLanguage, sourceLanguage));
            return Task.FromResult(new TranslationResult($"[{targetLanguage}] {text}", sourceLanguage ?? detected, targetLanguage));
        }
    }
}
