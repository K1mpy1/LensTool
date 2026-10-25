using System.Windows;
using LensTool.Ocr;

namespace LensTool.Tests;

public class OcrTextLayoutTests
{
    // Lines 20 px tall with 6 px spacing, like wrapped prose.
    private static OcrLine Line(string text, int index, double width = 400, double extraGap = 0) =>
        new(text, new Rect(10, 10 + index * 26 + extraGap, width, 20));

    [Fact]
    public void WrappedLinesJoinIntoOneParagraph()
    {
        var lines = new[]
        {
            Line("The quick brown fox jumps over", 0),
            Line("the lazy dog and runs away.", 1, width: 380),
        };

        Assert.Equal("The quick brown fox jumps over the lazy dog and runs away.", OcrTextLayout.ToParagraphs(lines));
    }

    [Fact]
    public void HyphenatedWordIsRejoined()
    {
        var lines = new[] { Line("This is an exam-", 0), Line("ple of hyphenation.", 1) };

        Assert.Equal("This is an example of hyphenation.", OcrTextLayout.ToParagraphs(lines));
    }

    [Fact]
    public void DashBeforeCapitalOrNonLetterIsKept()
    {
        var lines = new[] { Line("The same build covers both Windows-", 0), Line("Linux setups and more", 1) };

        Assert.Equal("The same build covers both Windows- Linux setups and more", OcrTextLayout.ToParagraphs(lines));
    }

    [Fact]
    public void LargeGapStartsNewParagraph()
    {
        var lines = new[] { Line("First paragraph line.", 0), Line("Second paragraph line.", 1, extraGap: 20) };

        Assert.Equal($"First paragraph line.{Environment.NewLine}Second paragraph line.", OcrTextLayout.ToParagraphs(lines));
    }

    [Fact]
    public void ShortLineEndsParagraph()
    {
        var lines = new[]
        {
            Line("A full line of text that wraps at the edge", 0),
            Line("ends here.", 1, width: 100),
            Line("Next paragraph starts right below.", 2),
        };

        Assert.Equal(
            $"A full line of text that wraps at the edge ends here.{Environment.NewLine}Next paragraph starts right below.",
            OcrTextLayout.ToParagraphs(lines));
    }

    [Fact]
    public void MenuItemsStaySeparate()
    {
        var lines = new[]
        {
            Line("Open", 0, width: 60),
            Line("Save as...", 1, width: 110),
            Line("Exit", 2, width: 50),
        };

        Assert.Equal(string.Join(Environment.NewLine, "Open", "Save as...", "Exit"), OcrTextLayout.ToParagraphs(lines));
    }

    [Fact]
    public void ProseWrapBeforeCapitalizedWordStillJoins()
    {
        var lines = new[] { Line("Yesterday I met an old friend named", 0), Line("John at the station.", 1, width: 250) };

        Assert.Equal("Yesterday I met an old friend named John at the station.", OcrTextLayout.ToParagraphs(lines));
    }

    [Fact]
    public void EmptyInputGivesEmptyString() => Assert.Equal("", OcrTextLayout.ToParagraphs([]));
}
