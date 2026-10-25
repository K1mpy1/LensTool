using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using WinOcrEngine = Windows.Media.Ocr.OcrEngine;
using WinOcrResult = Windows.Media.Ocr.OcrResult;

namespace LensTool.Ocr;

/// <summary>
/// OCR using the built-in Windows engine (Windows.Media.Ocr). Works offline;
/// available languages are those with installed Windows language packs.
/// </summary>
public sealed class WindowsOcrEngine : IOcrEngine
{
    // Windows OCR struggles with lines below ~20 px, while UI text at 100% is 12–16 px.
    // If lines come out small, the image is upscaled and recognized again.
    private const double MinLineHeight = 24;
    private const double TargetLineHeight = 40;
    private const double MaxScale = 4;

    // Share of Latin letters at which text is treated as English (auto mode).
    private const double LatinShareForFallback = 0.6;

    private readonly WinOcrEngine _engine;

    // A recognizer for a non-Latin language (ru etc.) misreads English,
    // so in auto mode Latin text is re-recognized with an English engine.
    private readonly WinOcrEngine? _latinFallback;

    /// <param name="languageTag">BCP-47 tag (e.g. "ru", "en-US"); null = auto from Windows profile languages.</param>
    public WindowsOcrEngine(string? languageTag = null)
    {
        var engine = languageTag is null
            ? WinOcrEngine.TryCreateFromUserProfileLanguages()
            : WinOcrEngine.IsLanguageSupported(new Language(languageTag))
                ? WinOcrEngine.TryCreateFromLanguage(new Language(languageTag))
                : null;

        _engine = engine ?? throw new OcrUnavailableException(languageTag is null
            ? "Для языков Windows не установлено распознавание текста (OCR). Добавьте языковой пакет в «Параметры → Время и язык»."
            : $"Язык OCR «{languageTag}» не установлен. Доступны: {string.Join(", ", AvailableLanguages)}.");

        if (languageTag is null && _engine.RecognizerLanguage.Script != "Latn")
        {
            var english = WinOcrEngine.AvailableRecognizerLanguages
                .FirstOrDefault(l => l.LanguageTag.StartsWith("en", StringComparison.OrdinalIgnoreCase));
            if (english is not null)
                _latinFallback = WinOcrEngine.TryCreateFromLanguage(english);
        }
    }

    public string Name => _latinFallback is null
        ? $"Windows OCR ({_engine.RecognizerLanguage.LanguageTag})"
        : $"Windows OCR ({_engine.RecognizerLanguage.LanguageTag}, латиница — {_latinFallback.RecognizerLanguage.LanguageTag})";

    public static IReadOnlyList<string> AvailableLanguages =>
        WinOcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag).ToList();

    public async Task<OcrResult> RecognizeAsync(BitmapSource image, CancellationToken cancellationToken = default)
    {
        if (!image.IsFrozen)
            throw new ArgumentException("Картинка должна быть заморожена (Freeze), т.к. обрабатывается в фоне.", nameof(image));

        var sw = Stopwatch.StartNew();
        var result = await RecognizeWithAsync(_engine, image, cancellationToken);

        if (_latinFallback is not null && LatinShare(result.Text) >= LatinShareForFallback)
        {
            var latin = await RecognizeWithAsync(_latinFallback, image, cancellationToken);
            if (!latin.IsEmpty)
                result = latin;
            AppLog.Info($"OCR: текст латиницей, перераспознан как {_latinFallback.RecognizerLanguage.LanguageTag}");
        }

        AppLog.Info($"OCR {result.Language}: {image.PixelWidth}x{image.PixelHeight}, строк {result.Lines.Count}, " +
                    $"символов {result.Text.Length}, {sw.ElapsedMilliseconds} мс");
        return result;
    }

    private static async Task<OcrResult> RecognizeWithAsync(WinOcrEngine engine, BitmapSource image, CancellationToken ct)
    {
        var result = await RecognizeScaledAsync(engine, image, 1, ct);

        double lineHeight = MedianLineHeight(result);
        double scale = lineHeight > 0
            ? Math.Ceiling(TargetLineHeight / lineHeight)
            : 2; // nothing found: the text may be too small
        scale = Math.Min(scale, Math.Min(MaxScale, MaxScaleFor(image)));

        if ((lineHeight == 0 || lineHeight < MinLineHeight) && scale >= 2)
        {
            var scaled = await RecognizeScaledAsync(engine, image, scale, ct);
            if (!scaled.IsEmpty || result.IsEmpty)
                result = scaled;
            AppLog.Info($"OCR: строки ~{lineHeight:0} px, повтор с увеличением x{scale}");
        }
        return result;
    }

    private static async Task<OcrResult> RecognizeScaledAsync(WinOcrEngine engine, BitmapSource image, double scale, CancellationToken ct)
    {
        using var bitmap = await Task.Run(() => ToSoftwareBitmap(image, scale), ct);
        WinOcrResult raw = await engine.RecognizeAsync(bitmap).AsTask(ct);

        var lines = new List<OcrLine>(raw.Lines.Count);
        foreach (var line in raw.Lines)
        {
            var bounds = Rect.Empty;
            foreach (var word in line.Words)
            {
                var r = word.BoundingRect;
                bounds.Union(new Rect(r.X / scale, r.Y / scale, r.Width / scale, r.Height / scale));
            }
            lines.Add(new OcrLine(line.Text, bounds));
        }
        return new OcrResult(lines, engine.RecognizerLanguage.LanguageTag);
    }

    /// <summary>Share of Latin letters among all letters of the text.</summary>
    private static double LatinShare(string text)
    {
        int letters = 0, latin = 0;
        foreach (char c in text)
        {
            if (!char.IsLetter(c))
                continue;
            letters++;
            if (c <= 'ɏ') // Basic Latin … Latin Extended-B
                latin++;
        }
        return letters == 0 ? 0 : (double)latin / letters;
    }

    private static SoftwareBitmap ToSoftwareBitmap(BitmapSource image, double scale)
    {
        BitmapSource source = image;
        if (scale > 1)
        {
            var transformed = new TransformedBitmap(source, new ScaleTransform(scale, scale));
            transformed.Freeze();
            source = transformed;
        }
        if (source.Format != PixelFormats.Bgra32)
        {
            var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
            converted.Freeze();
            source = converted;
        }

        int width = source.PixelWidth, height = source.PixelHeight, stride = width * 4;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);
        return SoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), BitmapPixelFormat.Bgra8, width, height, BitmapAlphaMode.Premultiplied);
    }

    private static double MaxScaleFor(BitmapSource image) =>
        Math.Floor(WinOcrEngine.MaxImageDimension / (double)Math.Max(image.PixelWidth, image.PixelHeight));

    private static double MedianLineHeight(OcrResult result)
    {
        if (result.IsEmpty)
            return 0;
        var heights = result.Lines.Select(l => l.Bounds.Height).Order().ToList();
        return heights[heights.Count / 2];
    }
}
