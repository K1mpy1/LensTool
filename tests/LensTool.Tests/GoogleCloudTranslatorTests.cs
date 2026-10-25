using System.Net;
using System.Net.Http;
using System.Text.Json;
using LensTool.Translation;

namespace LensTool.Tests;

public class GoogleCloudTranslatorTests
{
    private const string OkResponse =
        """{"data":{"translations":[{"translatedText":"Привет, \"мир\" & всё","detectedSourceLanguage":"en"}]}}""";

    [Fact]
    public async Task SendsRequestAndParsesResponse()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, OkResponse);
        var translator = new GoogleCloudTranslator(new HttpClient(handler), () => "g-key");

        var result = await translator.TranslateAsync("Hello, \"world\" & all", "RU");

        Assert.Equal(new TranslationResult("Привет, \"мир\" & всё", "EN", "RU"), result);
        Assert.Equal("https://translation.googleapis.com/language/translate/v2", handler.Request!.RequestUri!.ToString());
        // The key goes in a header, never in the URL.
        Assert.Equal("g-key", Assert.Single(handler.Request.Headers.GetValues("X-Goog-Api-Key")));
        Assert.DoesNotContain("g-key", handler.Request.RequestUri.ToString());

        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("Hello, \"world\" & all", body.RootElement.GetProperty("q")[0].GetString());
        Assert.Equal("ru", body.RootElement.GetProperty("target").GetString());
        Assert.Equal("text", body.RootElement.GetProperty("format").GetString());
        Assert.False(body.RootElement.TryGetProperty("source", out _));
    }

    [Fact]
    public async Task ExplicitSourceIsSentAndReturnedWhenNotDetected()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, """{"data":{"translations":[{"translatedText":"Hi"}]}}""");
        var translator = new GoogleCloudTranslator(new HttpClient(handler), () => "g-key");

        var result = await translator.TranslateAsync("Привет", "EN-US", "RU");

        Assert.Equal(new TranslationResult("Hi", "RU", "EN-US"), result);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal("en", body.RootElement.GetProperty("target").GetString());
        Assert.Equal("ru", body.RootElement.GetProperty("source").GetString());
    }

    [Theory]
    [InlineData("EN-US", "en")]
    [InlineData("RU", "ru")]
    [InlineData("PT-BR", "pt")]
    [InlineData("PT-PT", "pt-pt")]
    [InlineData("ZH-HANS", "zh-hans")]
    public void ConvertsLanguageCodes(string code, string expected) =>
        Assert.Equal(expected, GoogleCloudTranslator.ToGoogleCode(code));

    [Fact]
    public async Task MissingKeyThrowsUnavailable()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, OkResponse);
        var translator = new GoogleCloudTranslator(new HttpClient(handler), () => null);

        var ex = await Assert.ThrowsAsync<TranslatorUnavailableException>(() => translator.TranslateAsync("text", "RU"));

        Assert.Contains(GoogleCloudTranslator.ApiKeyVariable, ex.Message);
        Assert.Null(handler.Request);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, """{"error":{"message":"API key not valid. Please pass a valid API key."}}""", "не принял ключ")]
    [InlineData(HttpStatusCode.Forbidden, """{"error":{"status":"PERMISSION_DENIED","details":[{"reason":"SERVICE_DISABLED"}]}}""", "не включён")]
    [InlineData(HttpStatusCode.Forbidden, """{"error":{"message":"This API method requires billing to be enabled."}}""", "оплата")]
    [InlineData(HttpStatusCode.TooManyRequests, "{}", "квота")]
    [InlineData(HttpStatusCode.InternalServerError, "{}", "недоступен")]
    public async Task HttpErrorsBecomeReadableMessages(HttpStatusCode status, string body, string expectedFragment)
    {
        var translator = new GoogleCloudTranslator(new HttpClient(new FakeHandler(status, body)), () => "g-key");

        var ex = await Assert.ThrowsAsync<TranslationException>(() => translator.TranslateAsync("text", "RU"));

        Assert.Contains(expectedFragment, ex.Message);
    }

    /// <summary>Real Google call; runs only when GOOGLE_TRANSLATE_API_KEY is set.</summary>
    [EnvironmentFact(GoogleCloudTranslator.ApiKeyVariable)]
    public async Task LiveTranslation()
    {
        using var http = new HttpClient();
        var translator = new GoogleCloudTranslator(http, GoogleCloudTranslator.ReadApiKeyFromEnvironment);

        var result = await translator.TranslateAsync("Good morning", "RU");

        Assert.Equal("EN", result.DetectedSourceLanguage);
        Assert.Contains("утр", result.Text, StringComparison.OrdinalIgnoreCase);
    }
}
