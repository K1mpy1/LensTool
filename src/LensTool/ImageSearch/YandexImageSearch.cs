using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace LensTool.ImageSearch;

/// <summary>
/// Yandex Images: the app uploads the picture and gets a "cbir_id" back, then opens the results page.
/// Fully automatic, no consent dialogs or extra steps in the browser.
/// </summary>
public sealed class YandexImageSearch(HttpClient http) : IImageSearchProvider
{
    // The same endpoint the yandex.ru/images upload button posts to; format=json returns the id instead of HTML.
    private static readonly Uri UploadUri = new(
        "https://yandex.ru/images/search?rpt=imageview&format=json&request=" +
        Uri.EscapeDataString("""{"blocks":[{"block":"b-page_type_search-by-image__link"}]}"""));

    public string Name => "Yandex";

    public async Task<ImageSearchLaunch> PrepareAsync(BitmapSource image, CancellationToken cancellationToken = default)
    {
        byte[] jpeg = await Task.Run(() => SearchImage.ToJpeg(image), cancellationToken);

        var file = new ByteArrayContent(jpeg);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        using var request = new HttpRequestMessage(HttpMethod.Post, UploadUri)
        {
            Content = new MultipartFormDataContent { { file, "upfile", "image.jpg" } },
        };

        string body;
        try
        {
            using var response = await http.SendAsync(request, cancellationToken);
            AppLog.Info($"Yandex: HTTP {(int)response.StatusCode}, {jpeg.Length / 1024} КБ");
            if (!response.IsSuccessStatusCode)
                throw new ImageSearchException($"Яндекс не принял картинку (HTTP {(int)response.StatusCode}).");
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new ImageSearchException("Нет связи с Яндексом.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ImageSearchException("Яндекс не ответил вовремя.", ex);
        }

        string cbirId = ParseCbirId(body)
            ?? throw new ImageSearchException("Яндекс вернул неожиданный ответ.");
        return new ImageSearchLaunch(ResultsUrl(cbirId));
    }

    public static string ResultsUrl(string cbirId) =>
        $"https://yandex.ru/images/search?rpt=imageview&cbir_id={Uri.EscapeDataString(cbirId)}";

    /// <summary>Reads blocks[].params.cbirId from the upload response; null if it is not there.</summary>
    public static string? ParseCbirId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("blocks", out var blocks) || blocks.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var block in blocks.EnumerateArray())
            {
                if (block.TryGetProperty("params", out var p)
                    && p.TryGetProperty("cbirId", out var id)
                    && id.GetString() is { Length: > 0 } value)
                    return value;
            }
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
