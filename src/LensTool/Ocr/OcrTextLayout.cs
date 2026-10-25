namespace LensTool.Ocr;

/// <summary>
/// Turns OCR lines back into paragraphs. Translators work much better on whole sentences
/// than on text broken at the screen's line wraps.
/// </summary>
public static class OcrTextLayout
{
    // A vertical gap larger than this share of the line height starts a new paragraph.
    private const double ParagraphGapRatio = 0.75;

    private const int MaxListItemWords = 3;

    public static string ToParagraphs(IReadOnlyList<OcrLine> lines)
    {
        if (lines.Count == 0)
            return "";

        double lineHeight = lines.Select(l => l.Bounds.Height).Order().ElementAt(lines.Count / 2);
        double maxWidth = lines.Max(l => l.Bounds.Width);

        var paragraphs = new List<string>();
        var current = new System.Text.StringBuilder(lines[0].Text.Trim());
        for (int i = 1; i < lines.Count; i++)
        {
            var prev = lines[i - 1];
            var line = lines[i];
            string text = line.Text.Trim();

            double gap = line.Bounds.Top - prev.Bounds.Bottom;
            bool newParagraph = gap > lineHeight * ParagraphGapRatio
                                || EndedBeforeWrap(prev, line, maxWidth)
                                || LooksLikeListItem(prev, text);

            if (newParagraph)
            {
                paragraphs.Add(current.ToString());
                current.Clear().Append(text);
            }
            else if (EndsWithHyphenatedWord(current) && text.Length > 0 && char.IsLower(text[0]))
            {
                // "exam-" + "ple" -> "example"
                current.Length--;
                current.Append(text);
            }
            else
            {
                current.Append(' ').Append(text);
            }
        }
        paragraphs.Add(current.ToString());
        return string.Join(Environment.NewLine, paragraphs);
    }

    /// <summary>
    /// The next line's first word would have fit on the previous line, so the previous line was not
    /// wrapped: it ends a paragraph or is a standalone item (menu entry, list item).
    /// </summary>
    private static bool EndedBeforeWrap(OcrLine prev, OcrLine next, double maxWidth)
    {
        string text = next.Text.Trim();
        if (text.Length == 0)
            return false;

        int firstWordLength = text.IndexOf(' ') is var space and > 0 ? space : text.Length;
        double firstWordWidth = next.Bounds.Width * firstWordLength / text.Length;
        return prev.Bounds.Width + firstWordWidth < maxWidth;
    }

    /// <summary>
    /// A few words followed by a capitalized line: a menu or list entry. Needed for the widest
    /// line of a block, where <see cref="EndedBeforeWrap"/> has nothing to compare against.
    /// </summary>
    private static bool LooksLikeListItem(OcrLine prev, string nextText) =>
        nextText.Length > 0 && char.IsUpper(nextText[0])
        && prev.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= MaxListItemWords;

    private static bool EndsWithHyphenatedWord(System.Text.StringBuilder sb) =>
        sb.Length >= 2 && sb[^1] == '-' && char.IsLetter(sb[^2]);
}
