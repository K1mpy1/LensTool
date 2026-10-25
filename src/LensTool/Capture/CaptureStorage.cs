using System.IO;
using System.Windows.Media.Imaging;

namespace LensTool.Capture;

public enum CaptureMode
{
    Translate,
    ImageSearch,
}

/// <summary>Selection result: region in physical pixels, the cropped image and the monitor containing its center.</summary>
public sealed record CaptureResult(PixelRect Bounds, BitmapSource Image, MonitorInfo Monitor);

public static class CaptureStorage
{
    /// <summary>Saves the image as PNG and returns the full file path.</summary>
    public static string SavePng(BitmapSource image, string folder, CaptureMode mode)
    {
        Directory.CreateDirectory(folder);
        string suffix = mode == CaptureMode.Translate ? "translate" : "search";
        string path = Path.Combine(folder, $"LensTool_{DateTime.Now:yyyyMMdd_HHmmss_fff}_{suffix}.png");

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }
}
