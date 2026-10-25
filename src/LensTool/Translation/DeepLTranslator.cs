using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace LensTool.Translation;

/// <summary>
/// DeepL API (https://developers.deepl.com). Free keys end with ":fx" and use api-free.deepl.com.
/// The key is read on every call, so it can be set without restarting the app.
/// </summary>
public sealed class DeepLTranslator(HttpClient http, Func<string?> apiKeyProvider) : ITranslator
{
    public const string ApiKeyVariable = "DEEPL_API_KEY";

    private static readonly Uri FreeEndpoint = new("https://api-free.deepl.com/v2/translate");
    private static readonly Uri ProEndpoint = new("https://api.deepl.com/v2/translate");

    public string Name => "DeepL";

    /// <summary>Reads the key from the process environment, then from the user's persisted variables (setx).</summary>
    public static string? ReadApiKeyFromEnvironment() =>
        NullIfBlank(Environment.GetEnvironmentVariable(ApiKeyVariable))
        ?? NullIfBlank(Environment.GetEnvironmentVariable(ApiKeyVariable, EnvironmentVariableTarget.User));

    public async Task<TranslationResult> TranslateAsync(string text, string targetLanguage, string? sourceLanguage = null,
        CancellationToken cancellationToken = default)
    {
        string apiKey = apiKeyProvider()?.Trim() is { Length: > 0 } key
            ? key
            : throw new TranslatorUnavailableException(
                $"Не задан ключ DeepL. Укажите его в переменной окружения {ApiKeyVariable} и повторите.");

        var request = new HttpRequestMessage(HttpMethod.Post, apiKey.EndsWith(":fx", StringComparison.Ordinal) ? FreeEndpoint : ProEndpoint)
        {
            Content = JsonContent.Create(new DeepLRequest([text], targetLanguage.ToUpperInvariant(), sourceLanguage?.ToUpperInvariant())),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", apiKey);

        var sw = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new TranslationException("Нет связи с DeepL. Проверьте подключение к интернету.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationException("DeepL не ответил вовремя.", ex);
        }

        using (response)
        {
            AppLog.Info($"DeepL: HTTP {(int)response.StatusCode}, {text.Length} символов, {sw.ElapsedMilliseconds} мс");
            if (!response.IsSuccessStatusCode)
                throw new TranslationException(DescribeError(response.StatusCode));

            var body = await response.Content.ReadFromJsonAsync<DeepLResponse>(cancellationToken);
            var translation = body?.Translations is [var first, ..]
                ? first
                : throw new TranslationException("DeepL вернул пустой ответ.");
            return new TranslationResult(translation.Text, translation.DetectedSourceLanguage.ToUpperInvariant(),
                targetLanguage.ToUpperInvariant());
        }
    }

    private static string DescribeError(HttpStatusCode status) => (int)status switch
    {
        400 => "DeepL отклонил запрос (неверный язык или параметры).",
        401 or 403 => $"DeepL не принял ключ. Проверьте значение {ApiKeyVariable}.",
        429 => "Слишком много запросов к DeepL, попробуйте через несколько секунд.",
        456 => "Исчерпан месячный лимит символов DeepL.",
        >= 500 => "DeepL временно недоступен, попробуйте позже.",
        _ => $"Ошибка DeepL: HTTP {(int)status}.",
    };

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private sealed record DeepLRequest(
        [property: JsonPropertyName("text")] string[] Text,
        [property: JsonPropertyName("target_lang")] string TargetLang,
        [property: JsonPropertyName("source_lang"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourceLang);

    private sealed record DeepLResponse(
        [property: JsonPropertyName("translations")] DeepLTranslation[]? Translations);

    private sealed record DeepLTranslation(
        [property: JsonPropertyName("detected_source_language")] string DetectedSourceLanguage,
        [property: JsonPropertyName("text")] string Text);
}
