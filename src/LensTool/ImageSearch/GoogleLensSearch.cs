using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LensTool.ImageSearch;

/// <summary>
/// Google Lens. The app uploads the image itself and opens the results page in the browser.
/// In the EU Google first redirects to its cookie consent page; the app answers it with
/// "Accept all" (the user's choice) and retries. If anything goes wrong, the image is put on the
/// clipboard and Google opens with the Lens upload dialog, so the user can paste it with Ctrl+V.
/// </summary>
/// <param name="http">A client without automatic redirects and with a cookie container (see <see cref="CreateHttpClient"/>).</param>
public sealed partial class GoogleLensSearch(HttpClient http, Action<BitmapSource> copyToClipboard) : IImageSearchProvider
{
    public const string LensDialogUrl = "https://www.google.com/?olud"; // "olud" opens the Lens upload dialog
    private const string ConsentSaveUrl = "https://consent.google.com/save";
    private const int MaxSide = 2048; // Lens does not need more detail, and big uploads are slow
    private const int MaxRedirects = 5;

    public string Name => "Google";

    /// <summary>Lens answers with redirects and needs its consent cookie kept between requests.</summary>
    public static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, CookieContainer = new CookieContainer() })
        {
            Timeout = TimeSpan.FromSeconds(20),
        };
        // Google serves the Lens web flow only to browsers.
        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36");
        return client;
    }

    public async Task<ImageSearchLaunch> PrepareAsync(BitmapSource image, CancellationToken cancellationToken = default)
    {
        try
        {
            byte[] jpeg = await Task.Run(() => ToJpeg(image), cancellationToken);
            return new ImageSearchLaunch(await UploadAsync(jpeg, cancellationToken));
        }
        catch (Exception ex) when (ex is ImageSearchException or HttpRequestException
                                       || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            AppLog.Error("Google Lens: загрузка не удалась, открываю окно вставки", ex);
            copyToClipboard(image);
            string reason = ex is ImageSearchException ? ex.Message : "Нет связи с Google.";
            return new ImageSearchLaunch(LensDialogUrl, $"{reason} Картинка скопирована — вставьте её в Google Lens: Ctrl+V.");
        }
    }

    /// <summary>Uploads the image and returns the results URL, answering the consent page once if it appears.</summary>
    private async Task<string> UploadAsync(byte[] jpeg, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            using var response = await SendUploadAsync(jpeg, ct);
            string? location = response.Headers.Location?.ToString();
            AppLog.Info($"Google Lens: HTTP {(int)response.StatusCode}, {jpeg.Length / 1024} КБ");

            if (IsRedirect(response.StatusCode) && location is not null)
            {
                if (IsResultsUrl(location))
                    return location;
                if (attempt == 0 && new Uri(location).Host == "consent.google.com")
                {
                    await AcceptConsentAsync(location, ct);
                    continue;
                }
            }
            throw new ImageSearchException($"Google Lens ответил неожиданно (HTTP {(int)response.StatusCode}).");
        }
        throw new ImageSearchException("Google Lens снова запросил согласие на cookies.");
    }

    private Task<HttpResponseMessage> SendUploadAsync(byte[] jpeg, CancellationToken ct)
    {
        var file = new ByteArrayContent(jpeg);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        string lang = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        long stamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://lens.google.com/v3/upload?hl={lang}&re=df&ep=gsbubb&st={stamp}&vpw=1920&vph=1080")
        {
            Content = new MultipartFormDataContent { { file, "encoded_image", "image.jpg" } },
        };
        return http.SendAsync(request, ct);
    }

    /// <summary>Opens the consent page and submits its "Accept all" form; the cookie lands in the client's container.</summary>
    private async Task AcceptConsentAsync(string consentUrl, CancellationToken ct)
    {
        string html = await GetFollowingRedirectsAsync(consentUrl, ct);
        var fields = ParseAcceptForm(html)
            ?? throw new ImageSearchException("Не удалось разобрать окно согласия Google.");

        using var response = await http.PostAsync(ConsentSaveUrl, new FormUrlEncodedContent(fields), ct);
        AppLog.Info($"Google: согласие на cookies — «Принять все», HTTP {(int)response.StatusCode}");
        if (!IsRedirect(response.StatusCode) && !response.IsSuccessStatusCode)
            throw new ImageSearchException($"Google не принял согласие на cookies (HTTP {(int)response.StatusCode}).");
    }

    private async Task<string> GetFollowingRedirectsAsync(string url, CancellationToken ct)
    {
        for (int i = 0; i < MaxRedirects; i++)
        {
            using var response = await http.GetAsync(url, ct);
            if (IsRedirect(response.StatusCode) && response.Headers.Location is { } next)
            {
                url = new Uri(new Uri(url), next).ToString();
                continue;
            }
            return await response.Content.ReadAsStringAsync(ct);
        }
        throw new ImageSearchException("Слишком много перенаправлений на странице согласия Google.");
    }

    /// <summary>
    /// Hidden fields of the "Accept all" form on consent.google.com: it posts to /save with set_sc=true,
    /// set_aps=true and set_eom=false, while "Reject all" carries only set_eom=true. Null if no such form is found.
    /// </summary>
    public static IReadOnlyList<KeyValuePair<string, string>>? ParseAcceptForm(string html)
    {
        foreach (Match form in ConsentForm().Matches(html))
        {
            var fields = HiddenInput().Matches(form.Groups["body"].Value)
                .Select(m => KeyValuePair.Create(WebUtility.HtmlDecode(m.Groups["name"].Value), WebUtility.HtmlDecode(m.Groups["value"].Value)))
                .ToList();
            bool acceptsAll = fields.Any(f => f is { Key: "set_sc", Value: "true" })
                              && fields.Any(f => f is { Key: "set_aps", Value: "true" })
                              && fields.Any(f => f is { Key: "set_eom", Value: "false" });
            if (acceptsAll)
                return fields;
        }
        return null;
    }

    /// <summary>A Lens results page: google.com/search with a visual search id.</summary>
    public static bool IsResultsUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && uri.Host is "www.google.com" or "google.com"
        && uri.AbsolutePath == "/search"
        && uri.Query.Contains("vsrid=", StringComparison.Ordinal);

    private static bool IsRedirect(HttpStatusCode status) => (int)status is >= 300 and < 400;

    private static byte[] ToJpeg(BitmapSource image)
    {
        BitmapSource source = image;
        int longer = Math.Max(image.PixelWidth, image.PixelHeight);
        if (longer > MaxSide)
        {
            var scaled = new TransformedBitmap(image, new ScaleTransform((double)MaxSide / longer, (double)MaxSide / longer));
            scaled.Freeze();
            source = scaled;
        }
        var encoder = new JpegBitmapEncoder { QualityLevel = 92 };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    [GeneratedRegex("""<form[^>]*action="https://consent\.google\.com/save"[^>]*>(?<body>.*?)</form>""", RegexOptions.Singleline)]
    private static partial Regex ConsentForm();

    // Google writes type before name/value; attribute order is matched as it appears in the page.
    [GeneratedRegex("""<input[^>]*type="hidden"[^>]*name="(?<name>[^"]*)"[^>]*value="(?<value>[^"]*)"[^>]*>""")]
    private static partial Regex HiddenInput();
}
