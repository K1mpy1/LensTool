using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LensTool.Ocr;

namespace LensTool.Tests;

/// <summary>Integration tests: require Windows OCR with the ru and en-US languages.</summary>
public class WindowsOcrEngineTests
{
    [Theory]
    [InlineData("en-US", "The quick brown fox jumps over the lazy dog", 20)]
    [InlineData("ru", "Съешь же ещё этих мягких французских булок", 20)]
    // Small UI font: checks that upscaling kicks in.
    [InlineData("en-US", "Small interface text should still be readable", 11)]
    [InlineData("ru", "Мелкий текст интерфейса тоже читается", 11)]
    public async Task RecognizesRenderedText(string language, string text, double fontSize)
    {
        var engine = new WindowsOcrEngine(language);

        var result = await engine.RecognizeAsync(RenderText(text, fontSize));

        Assert.Equal(Normalize(text), Normalize(result.Text));
        Assert.Equal(language, result.Language);
    }

    // Auto mode: the profile language on this machine is ru, so English text must go to en-US.
    [Theory]
    [InlineData("The quick brown fox jumps over the lazy dog", "en-US")]
    [InlineData("Съешь же ещё этих мягких французских булок", "ru")]
    [InlineData("Нажмите Save, чтобы сохранить файл", "ru")]
    public async Task AutoModePicksEngineByScript(string text, string expectedLanguage)
    {
        var result = await new WindowsOcrEngine().RecognizeAsync(RenderText(text, 14));

        Assert.Equal(Normalize(text), Normalize(result.Text));
        Assert.Equal(expectedLanguage, result.Language);
    }

    [Fact]
    public async Task ReturnsLinesWithBoundsInSourcePixels()
    {
        var image = RenderText("First line\nSecond line", 24);

        var result = await new WindowsOcrEngine("en-US").RecognizeAsync(image);

        Assert.Equal(2, result.Lines.Count);
        var imageRect = new Rect(0, 0, image.PixelWidth, image.PixelHeight);
        Assert.All(result.Lines, l => Assert.True(imageRect.Contains(l.Bounds), $"{l.Bounds} вне {imageRect}"));
        Assert.True(result.Lines[0].Bounds.Bottom <= result.Lines[1].Bounds.Top);
    }

    [Fact]
    public async Task EmptyImageGivesEmptyResult()
    {
        var result = await new WindowsOcrEngine("en-US").RecognizeAsync(RenderText("", 20));

        Assert.True(result.IsEmpty);
        Assert.Equal("", result.Text);
    }

    [Fact]
    public void UnknownLanguageThrows()
    {
        var ex = Assert.Throws<OcrUnavailableException>(() => new WindowsOcrEngine("xx-XX"));
        Assert.Contains("xx-XX", ex.Message);
    }

    private static string Normalize(string s) =>
        string.Join(" ", s.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Renders black text on white, like on screen at 100% scale.</summary>
    private static BitmapSource RenderText(string text, double fontSize)
    {
        BitmapSource? result = null;
        var thread = new Thread(() =>
        {
            var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), fontSize, Brushes.Black, 1.0);
            int width = (int)Math.Ceiling(formatted.Width) + 40;
            int height = (int)Math.Ceiling(formatted.Height) + 40;

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
                dc.DrawText(formatted, new Point(20, 20));
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            result = bitmap;
        });
        thread.SetApartmentState(ApartmentState.STA); // WPF rendering requires STA
        thread.Start();
        thread.Join();
        return result!;
    }
}
