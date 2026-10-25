using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace LensTool.Translation;

/// <summary>
/// Google Cloud Translation API, Basic edition (v2 REST, https://cloud.google.com/translate/docs/reference/rest/v2/translate).
/// The key is sent in the X-Goog-Api-Key header (not in the URL) and is read on every call.
/// </summary>
public sealed class GoogleCloudTranslator(HttpClient http, Func<string?> apiKeyProvider) : ITranslator
{
    public const string ApiKeyVariable = "GOOGLE_TRANSLATE_API_KEY";

    private static readonly Uri Endpoint = new("https://translation.googleapis.com/language/translate/v2");

    public string Name => "Google";

    public static string? ReadApiKeyFromEnvironment() => ApiKeys.Read(ApiKeyVariable);

    public async Task<TranslationResult> TranslateAsync(string text, string targetLanguage, string? sourceLanguage = null,
        CancellationToken cancellationToken = default)
    {
        string apiKey = apiKeyProvider()?.Trim() is { Length: > 0 } key
            ? key
            : throw new TranslatorUnavailableException(
                $"Не задан ключ Google. Укажите его в переменной окружения {ApiKeyVariable} и повторите.");

        var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            // format=text: otherwise Google treats input as HTML and escapes quotes/ampersands in the output.
            Content = JsonContent.Create(new GoogleRequest([text], ToGoogleCode(targetLanguage),
                sourceLanguage is null ? null : ToGoogleCode(sourceLanguage), "text")),
        };
        request.Headers.Add("X-Goog-Api-Key", apiKey);

        var sw = Stopwatch.StartNew();
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new TranslationException("Нет связи с Google. Проверьте подключение к интернету.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TranslationException("Google не ответил вовремя.", ex);
        }

        using (response)
        {
            AppLog.Info($"Google: HTTP {(int)response.StatusCode}, {text.Length} символов, {sw.ElapsedMilliseconds} мс");
            if (!response.IsSuccessStatusCode)
                throw new TranslationException(DescribeError(response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken)));

            var body = await response.Content.ReadFromJsonAsync<GoogleResponse>(cancellationToken);
            var translation = body?.Data?.Translations is [var first, ..]
                ? first
                : throw new TranslationException("Google вернул пустой ответ.");

            // detectedSourceLanguage is absent when the source was given explicitly.
            string detected = translation.DetectedSourceLanguage ?? sourceLanguage ?? "";
            return new TranslationResult(translation.TranslatedText, detected.ToUpperInvariant(), targetLanguage.ToUpperInvariant());
        }
    }

    /// <summary>DeepL-style codes ("EN-US", "RU") to Google codes ("en", "ru"); Chinese variants are kept.</summary>
    public static string ToGoogleCode(string code)
    {
        string lower = code.ToLowerInvariant();
        return lower.StartsWith("zh", StringComparison.Ordinal) || lower == "pt-pt" ? lower : lower.Split('-')[0];
    }

    private static string DescribeError(HttpStatusCode status, string body)
    {
        if (body.Contains("API_KEY_INVALID", StringComparison.Ordinal) || body.Contains("API key not valid", StringComparison.Ordinal))
            return $"Google не принял ключ. Проверьте значение {ApiKeyVariable}.";
        if (body.Contains("SERVICE_DISABLED", StringComparison.Ordinal) || body.Contains("has not been used", StringComparison.Ordinal))
            return "Cloud Translation API не включён в проекте Google Cloud.";
        if (body.Contains("BILLING", StringComparison.OrdinalIgnoreCase))
            return "В проекте Google Cloud не подключена оплата (нужна даже для бесплатного лимита).";

        return (int)status switch
        {
            400 => "Google отклонил запрос (неверный язык или параметры).",
            401 or 403 => $"Google не принял ключ или у него нет доступа к Translation API ({ApiKeyVariable}).",
            429 => "Превышена квота или частота запросов к Google.",
            >= 500 => "Google временно недоступен, попробуйте позже.",
            _ => $"Ошибка Google: HTTP {(int)status}.",
        };
    }

    private sealed record GoogleRequest(
        [property: JsonPropertyName("q")] string[] Q,
        [property: JsonPropertyName("target")] string Target,
        [property: JsonPropertyName("source"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Source,
        [property: JsonPropertyName("format")] string Format);

    private sealed record GoogleResponse(
        [property: JsonPropertyName("data")] GoogleData? Data);

    private sealed record GoogleData(
        [property: JsonPropertyName("translations")] GoogleTranslation[]? Translations);

    private sealed record GoogleTranslation(
        [property: JsonPropertyName("translatedText")] string TranslatedText,
        [property: JsonPropertyName("detectedSourceLanguage")] string? DetectedSourceLanguage);
}
