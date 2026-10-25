using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using LensTool.Translation;

namespace LensTool.Tests;

public class DeepLTranslatorTests
{
    private const string OkResponse = """{"translations":[{"detected_source_language":"EN","text":"Привет, мир"}]}""";

    [Fact]
    public async Task SendsRequestAndParsesResponse()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, OkResponse);
        var translator = new DeepLTranslator(new HttpClient(handler), () => "secret:fx");

        var result = await translator.TranslateAsync("Hello, world", "ru");

        Assert.Equal(new TranslationResult("Привет, мир", "EN", "RU"), result);
        Assert.Equal("https://api-free.deepl.com/v2/translate", handler.Request!.RequestUri!.ToString());
        Assert.Equal("DeepL-Auth-Key secret:fx", handler.Request.Headers.Authorization!.ToString());

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("Hello, world", body.RootElement.GetProperty("text")[0].GetString());
        Assert.Equal("RU", body.RootElement.GetProperty("target_lang").GetString());
        Assert.False(body.RootElement.TryGetProperty("source_lang", out _));
    }

    [Fact]
    public async Task ProKeyUsesProEndpointAndPassesSourceLanguage()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, OkResponse);
        var translator = new DeepLTranslator(new HttpClient(handler), () => "pro-key");

        await translator.TranslateAsync("text", "EN-US", "ru");

        Assert.Equal("https://api.deepl.com/v2/translate", handler.Request!.RequestUri!.ToString());
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("RU", body.RootElement.GetProperty("source_lang").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public async Task MissingKeyThrowsUnavailable(string? key)
    {
        var handler = new FakeHandler(HttpStatusCode.OK, OkResponse);
        var translator = new DeepLTranslator(new HttpClient(handler), () => key);

        var ex = await Assert.ThrowsAsync<TranslatorUnavailableException>(() => translator.TranslateAsync("text", "RU"));

        Assert.Contains(DeepLTranslator.ApiKeyVariable, ex.Message);
        Assert.Null(handler.Request);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "ключ")]
    [InlineData((HttpStatusCode)456, "лимит")]
    [InlineData(HttpStatusCode.TooManyRequests, "Слишком много")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "недоступен")]
    public async Task HttpErrorsBecomeReadableMessages(HttpStatusCode status, string expectedFragment)
    {
        var translator = new DeepLTranslator(new HttpClient(new FakeHandler(status, "{}")), () => "k:fx");

        var ex = await Assert.ThrowsAsync<TranslationException>(() => translator.TranslateAsync("text", "RU"));

        Assert.Contains(expectedFragment, ex.Message);
    }

    [Fact]
    public async Task NetworkFailureBecomesTranslationException()
    {
        var translator = new DeepLTranslator(new HttpClient(new FakeHandler(new HttpRequestException("offline"))), () => "k:fx");

        var ex = await Assert.ThrowsAsync<TranslationException>(() => translator.TranslateAsync("text", "RU"));

        Assert.Contains("Нет связи", ex.Message);
    }

    /// <summary>Real DeepL call; runs only when DEEPL_API_KEY is set.</summary>
    [EnvironmentFact(DeepLTranslator.ApiKeyVariable)]
    public async Task LiveTranslation()
    {
        using var http = new HttpClient();
        var translator = new DeepLTranslator(http, DeepLTranslator.ReadApiKeyFromEnvironment);

        var result = await translator.TranslateAsync("Good morning", "RU");

        Assert.Equal("EN", result.DetectedSourceLanguage);
        Assert.Contains("утр", result.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UsageIsReadFromDeepL()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """{"character_count":12345,"character_limit":500000}""");
        var translator = new DeepLTranslator(new HttpClient(handler), () => "k:fx");

        var usage = await translator.GetUsageAsync();

        Assert.Equal(new DeepLUsage(12345, 500000), usage);
        Assert.Equal("https://api-free.deepl.com/v2/usage", handler.Request!.RequestUri!.ToString());
    }

    [Fact]
    public async Task UsageIsNullWithoutKeyOrOnError()
    {
        Assert.Null(await new DeepLTranslator(new HttpClient(new FakeHandler(HttpStatusCode.OK, "{}")), () => null).GetUsageAsync());
        Assert.Null(await new DeepLTranslator(new HttpClient(new FakeHandler(HttpStatusCode.Forbidden, "")), () => "k").GetUsageAsync());
        Assert.Null(await new DeepLTranslator(new HttpClient(new FakeHandler(new HttpRequestException())), () => "k").GetUsageAsync());
    }
}
