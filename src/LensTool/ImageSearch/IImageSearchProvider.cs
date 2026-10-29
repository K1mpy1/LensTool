using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LensTool.ImageSearch;

/// <summary>Reverse image search. Implementations are swappable (Yandex, Google…).</summary>
public interface IImageSearchProvider
{
    /// <summary>Provider name for logs and settings.</summary>
    string Name { get; }

    /// <summary>Prepares the search and returns what to open in the browser.</summary>
    Task<ImageSearchLaunch> PrepareAsync(BitmapSource image, CancellationToken cancellationToken = default);
}

/// <param name="Target">URL to open in the default browser.</param>
/// <param name="Hint">What the user still has to do in the browser, if anything.</param>
public sealed record ImageSearchLaunch(string Target, string? Hint = null);

/// <summary>The search could not be prepared; <see cref="Exception.Message"/> is shown to the user.</summary>
public sealed class ImageSearchException(string message, Exception? inner = null) : Exception(message, inner);

public static class SearchImage
{
    // Search engines do not need more detail than this, and big uploads are slow.
    public const int MaxSide = 2048;

    /// <summary>JPEG for upload, downscaled so the longer side is at most <see cref="MaxSide"/>.</summary>
    public static byte[] ToJpeg(BitmapSource image, int maxSide = MaxSide, int quality = 92)
    {
        BitmapSource source = image;
        int longer = Math.Max(image.PixelWidth, image.PixelHeight);
        if (longer > maxSide)
        {
            double scale = (double)maxSide / longer;
            var scaled = new TransformedBitmap(image, new ScaleTransform(scale, scale));
            scaled.Freeze();
            source = scaled;
        }

        var encoder = new JpegBitmapEncoder { QualityLevel = quality };
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }
}
