using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LensTool.ImageSearch;

namespace LensTool.Tests;

public class ImageSearchTests
{
    private const string UploadResponse =
        """{"cnt":"pageview_candidate","blocks":[{"name":{"block":"b-page_type_search-by-image__link"},"params":{"originalImageUrl":"https://avatars.mds.yandex.net/x/orig","cbirId":"4178369/43K4AgEjel46"}}]}""";

    [Fact]
    public async Task YandexUploadsJpegAndOpensResultsPage()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, UploadResponse);
        var search = new YandexImageSearch(new HttpClient(handler));

        var launch = await search.PrepareAsync(SolidImage(300, 200));

        Assert.Equal("https://yandex.ru/images/search?rpt=imageview&cbir_id=4178369%2F43K4AgEjel46", launch.Target);
        Assert.Null(launch.Hint);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.StartsWith("https://yandex.ru/images/search?rpt=imageview&format=json", handler.Request.RequestUri!.ToString());
        Assert.Contains("name=upfile", handler.Body);
        Assert.Contains("image/jpeg", handler.Body);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"blocks":[]}""", "неожиданный ответ")]
    [InlineData(HttpStatusCode.OK, "<html>captcha</html>", "неожиданный ответ")]
    [InlineData(HttpStatusCode.TooManyRequests, "", "HTTP 429")]
    public async Task YandexFailuresBecomeReadableErrors(HttpStatusCode status, string body, string expectedFragment)
    {
        var search = new YandexImageSearch(new HttpClient(new FakeHandler(status, body)));

        var ex = await Assert.ThrowsAsync<ImageSearchException>(() => search.PrepareAsync(SolidImage(50, 50)));

        Assert.Contains(expectedFragment, ex.Message);
    }

    [Fact]
    public async Task YandexNetworkFailure()
    {
        var search = new YandexImageSearch(new HttpClient(new FakeHandler(new HttpRequestException("offline"))));

        var ex = await Assert.ThrowsAsync<ImageSearchException>(() => search.PrepareAsync(SolidImage(50, 50)));

        Assert.Contains("Нет связи", ex.Message);
    }

    [Fact]
    public async Task GoogleLensCopiesImageAndOpensLensDialog()
    {
        BitmapSource? copied = null;
        var image = SolidImage(40, 30);

        var launch = await new GoogleLensSearch(img => copied = img).PrepareAsync(image);

        Assert.Same(image, copied);
        Assert.Equal("https://www.google.com/?olud", launch.Target);
        Assert.Contains("Ctrl+V", launch.Hint);
    }

    [Fact]
    public void LargeImageIsDownscaledToMaxSide()
    {
        byte[] jpeg = SearchImage.ToJpeg(SolidImage(5000, 1000));

        var decoded = BitmapDecoder.Create(new MemoryStream(jpeg), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        Assert.Equal(SearchImage.MaxSide, decoded.PixelWidth);
        Assert.Equal(410, decoded.PixelHeight, tolerance: 1);
    }

    [Fact]
    public void SmallImageKeepsItsSize()
    {
        byte[] jpeg = SearchImage.ToJpeg(SolidImage(300, 200));

        var decoded = BitmapDecoder.Create(new MemoryStream(jpeg), BitmapCreateOptions.None, BitmapCacheOption.OnLoad).Frames[0];
        Assert.Equal((300, 200), (decoded.PixelWidth, decoded.PixelHeight));
    }

    private static BitmapSource SolidImage(int width, int height)
    {
        var pixels = new byte[width * height * 4];
        Array.Fill(pixels, (byte)0x80);
        var image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgr32, null, pixels, width * 4);
        image.Freeze();
        return image;
    }
}
