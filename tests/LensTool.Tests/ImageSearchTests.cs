using System.Windows.Media;
using System.Windows.Media.Imaging;
using LensTool.ImageSearch;

namespace LensTool.Tests;

public class ImageSearchTests
{
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
    public async Task ClipboardFailureIsReported()
    {
        var search = new GoogleLensSearch(_ => throw new ImageSearchException("буфер занят"));

        var ex = await Assert.ThrowsAsync<ImageSearchException>(() => search.PrepareAsync(SolidImage(10, 10)));

        Assert.Contains("буфер", ex.Message);
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
