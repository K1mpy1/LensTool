using System.Windows;
using System.Windows.Media.Imaging;

namespace LensTool.Ocr;

/// <summary>Text recognition on an image. Implementations are swappable (Windows OCR, Tesseract, cloud…).</summary>
public interface IOcrEngine
{
    /// <summary>Engine name for logs and settings.</summary>
    string Name { get; }

    Task<OcrResult> RecognizeAsync(BitmapSource image, CancellationToken cancellationToken = default);
}

/// <summary>A line of text; <see cref="Bounds"/> is in source image pixels.</summary>
public sealed record OcrLine(string Text, Rect Bounds);

public sealed record OcrResult(IReadOnlyList<OcrLine> Lines, string Language)
{
    public string Text => string.Join(Environment.NewLine, Lines.Select(l => l.Text));

    public bool IsEmpty => Lines.Count == 0;
}

/// <summary>The engine cannot be used (e.g. the OCR language pack is not installed).</summary>
public sealed class OcrUnavailableException(string message) : Exception(message);
