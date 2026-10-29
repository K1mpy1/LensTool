using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LensTool.ImageSearch;

namespace LensTool.Tests;

public class ImageSearchTests
{
    private const string ResultsUrl = "https://www.google.com/search?vsrid=CM6Duc&udm=26&lns_mode=un&hl=ru";
    private const string ConsentUrl = "https://consent.google.com/m?continue=https://lens.google.com/v3/upload&gl=AT";

    // Shape of the real consent page: "Reject all" (set_eom=true) first, then "Accept all".
    private const string ConsentPage = """
        <html><body>
        <form action="https://consent.google.com/save" method="POST" style="display:inline;">
          <input type="hidden" name="gl" value="AT"><input type="hidden" name="continue" value="https://lens.google.com/v3/upload?hl=ru&amp;re=df">
          <input type="hidden" name="escs" value="AUjHK"><input type="hidden" name="set_eom" value="true" required>
          <button>Отклонить все</button>
        </form>
        <form action="https://consent.google.com/save" method="POST" style="display:inline;">
          <input type="hidden" name="gl" value="AT"><input type="hidden" name="escs" value="AUjHK">
          <input type="hidden" name="set_sc" value="true" required><input type="hidden" name="set_aps" value="true" required>
          <input type="hidden" name="set_eom" value="false" required>
          <button>Принять все</button>
        </form>
        </body></html>
        """;

    [Fact]
    public async Task UploadOpensResultsPageDirectly()
    {
        var handler = new QueueHandler(Redirect(HttpStatusCode.SeeOther, ResultsUrl));
        BitmapSource? copied = null;
        var image = SolidImage(300, 200);

        var launch = await new GoogleLensSearch(new HttpClient(handler), img => copied = img).PrepareAsync(image);

        Assert.Equal(ResultsUrl, launch.Target);
        Assert.Null(launch.Hint);
        Assert.Same(image, copied); // also on the clipboard, for pasting elsewhere
        var upload = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, upload.Method);
        Assert.StartsWith("https://lens.google.com/v3/upload?", upload.Uri);
        Assert.Contains("name=encoded_image", upload.Body);
        Assert.Contains("image/jpeg", upload.Body);
    }

    [Fact]
    public async Task ConsentPageIsAnsweredWithAcceptAllAndUploadIsRetried()
    {
        var handler = new QueueHandler(
            Redirect(HttpStatusCode.Found, ConsentUrl),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ConsentPage, Encoding.UTF8, "text/html") },
            Redirect(HttpStatusCode.SeeOther, "https://lens.google.com/v3/upload?hl=ru"),
            Redirect(HttpStatusCode.SeeOther, ResultsUrl));

        var launch = await new GoogleLensSearch(new HttpClient(handler), _ => { }).PrepareAsync(SolidImage(100, 100));

        Assert.Equal(ResultsUrl, launch.Target);
        Assert.Equal(["POST upload", "GET consent", "POST save", "POST upload"], handler.Requests.Select(r => r.Summary));
        string consent = handler.Requests[2].Body!;
        Assert.Contains("set_sc=true", consent);
        Assert.Contains("set_aps=true", consent);
        Assert.Contains("set_eom=false", consent);
        Assert.DoesNotContain("set_eom=true", consent);
        Assert.Contains("escs=AUjHK", consent);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task UnexpectedAnswerFallsBackToClipboardAndLensDialog(HttpStatusCode status)
    {
        BitmapSource? copied = null;
        var image = SolidImage(40, 30);
        var search = new GoogleLensSearch(new HttpClient(new QueueHandler(new HttpResponseMessage(status))), img => copied = img);

        var launch = await search.PrepareAsync(image);

        Assert.Same(image, copied);
        Assert.Equal(GoogleLensSearch.LensDialogUrl, launch.Target);
        Assert.Contains("Ctrl+V", launch.Hint);
    }

    [Fact]
    public async Task NetworkFailureFallsBackToClipboard()
    {
        BitmapSource? copied = null;
        var search = new GoogleLensSearch(new HttpClient(new FakeHandler(new HttpRequestException("offline"))), img => copied = img);

        var launch = await search.PrepareAsync(SolidImage(10, 10));

        Assert.NotNull(copied);
        Assert.Equal(GoogleLensSearch.LensDialogUrl, launch.Target);
        Assert.Contains("Нет связи", launch.Hint);
    }

    [Fact]
    public async Task ClipboardFailureDoesNotBlockUpload()
    {
        var handler = new QueueHandler(Redirect(HttpStatusCode.SeeOther, ResultsUrl));
        var search = new GoogleLensSearch(new HttpClient(handler), _ => throw new ImageSearchException("буфер занят"));

        var launch = await search.PrepareAsync(SolidImage(10, 10));

        Assert.Equal(ResultsUrl, launch.Target);
    }

    [Fact]
    public async Task UploadAndClipboardBothFailingIsReported()
    {
        var search = new GoogleLensSearch(new HttpClient(new QueueHandler(new HttpResponseMessage(HttpStatusCode.OK))),
            _ => throw new ImageSearchException("буфер занят"));

        var ex = await Assert.ThrowsAsync<ImageSearchException>(() => search.PrepareAsync(SolidImage(10, 10)));

        Assert.Contains("буфер обмена", ex.Message);
    }

    [Fact]
    public async Task ConsentAskedTwiceFallsBack()
    {
        var handler = new QueueHandler(
            Redirect(HttpStatusCode.Found, ConsentUrl),
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(ConsentPage) },
            Redirect(HttpStatusCode.SeeOther, "https://lens.google.com/v3/upload"),
            Redirect(HttpStatusCode.Found, ConsentUrl));

        var launch = await new GoogleLensSearch(new HttpClient(handler), _ => { }).PrepareAsync(SolidImage(10, 10));

        Assert.Equal(GoogleLensSearch.LensDialogUrl, launch.Target);
        Assert.Equal(4, handler.Requests.Count);
    }

    [Fact]
    public void AcceptFormIsPicked()
    {
        var fields = GoogleLensSearch.ParseAcceptForm(ConsentPage)!;

        Assert.Contains(KeyValuePair.Create("set_sc", "true"), fields);
        Assert.Contains(KeyValuePair.Create("set_aps", "true"), fields);
        Assert.Contains(KeyValuePair.Create("set_eom", "false"), fields);
        Assert.Contains(KeyValuePair.Create("escs", "AUjHK"), fields);
    }

    [Fact]
    public void HtmlEntitiesInFieldsAreDecoded()
    {
        const string page = """
            <form action="https://consent.google.com/save" method="POST">
            <input type="hidden" name="continue" value="https://lens.google.com/v3/upload?hl=ru&amp;re=df">
            <input type="hidden" name="set_sc" value="true"><input type="hidden" name="set_aps" value="true">
            <input type="hidden" name="set_eom" value="false"></form>
            """;

        var fields = GoogleLensSearch.ParseAcceptForm(page)!;

        Assert.Contains(KeyValuePair.Create("continue", "https://lens.google.com/v3/upload?hl=ru&re=df"), fields);
    }

    [Fact]
    public void NoAcceptFormGivesNull() =>
        Assert.Null(GoogleLensSearch.ParseAcceptForm("<html><form action=\"https://example.com\"></form></html>"));

    [Theory]
    [InlineData(ResultsUrl, true)]
    [InlineData("https://www.google.com/search?q=cats", false)]
    [InlineData("https://consent.google.com/m?continue=x", false)]
    [InlineData("https://evil.example/search?vsrid=1", false)]
    public void RecognizesResultsUrl(string url, bool expected) => Assert.Equal(expected, GoogleLensSearch.IsResultsUrl(url));

    private static HttpResponseMessage Redirect(HttpStatusCode status, string location)
    {
        var response = new HttpResponseMessage(status);
        response.Headers.Location = new Uri(location);
        return response;
    }

    private static BitmapSource SolidImage(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, (byte)0x80);
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }

    /// <summary>Returns the given responses in order and records each request.</summary>
    private sealed class QueueHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<(HttpMethod Method, string Uri, string? Body, string Summary)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            string uri = request.RequestUri!.ToString();
            var u = request.RequestUri;
            string target = u.Host == "lens.google.com" ? "upload" : u.AbsolutePath == "/save" ? "save" : "consent";
            Requests.Add((request.Method, uri, body, $"{request.Method} {target}"));
            return _responses.Count > 0 ? _responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.InternalServerError);
        }
    }
}
