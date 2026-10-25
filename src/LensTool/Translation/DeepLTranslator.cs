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

    private static readonly Uri FreeHost = new("https://api-free.deepl.com/");
    private static readonly Uri ProHost = new("https://api.deepl.com/");

    public string Name => "DeepL";

    public static string? ReadApiKeyFromEnvironment() => ApiKeys.Read(ApiKeyVariable);

    /// <summary>
    /// Characters used in the current DeepL billing period, as reported by DeepL itself
    /// (the period starts on the subscription date, not on the 1st). Null without a key or on any error.
    /// </summary>
    public async Task<DeepLUsage?> GetUsageAsync(CancellationToken cancellationToken = default)
    {
        if (apiKeyProvider()?.Trim() is not { Length: > 0 } apiKey)
            return null;

        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(HostFor(apiKey), "v2/usage"));
        request.Headers.Authorization = new AuthenticationHeaderValue("DeepL-Auth-Key", apiKey);
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                AppLog.Info($"DeepL usage: HTTP {(int)response.StatusCode}");
                return null;
            }
            return await response.Content.ReadFromJsonAsync<DeepLUsage>(cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            AppLog.Info($"DeepL usage недоступен: {ex.Message}");
            return null;
        }
    }

    private static Uri HostFor(string apiKey) => apiKey.EndsWith(":fx", StringComparison.Ordinal) ? FreeHost : ProHost;

    public async Task<TranslationResult> TranslateAsync(string text, string targetLanguage, string? sourceLanguage = null,
        CancellationToken cancellationToken = default)
    {
        string apiKey = apiKeyProvider()?.Trim() is { Length: > 0 } key
            ? key
            : throw new TranslatorUnavailableException(
                $"Не задан ключ DeepL. Укажите его в переменной окружения {ApiKeyVariable} и повторите.");

        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(HostFor(apiKey), "v2/translate"))
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

public sealed record DeepLUsage(
    [property: JsonPropertyName("character_count")] long CharacterCount,
    [property: JsonPropertyName("character_limit")] long CharacterLimit);
