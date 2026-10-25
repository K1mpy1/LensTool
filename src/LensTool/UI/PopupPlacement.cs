using LensTool.Capture;

namespace LensTool.UI;

/// <summary>Where to put a popup next to a screen region. Everything is in physical pixels.</summary>
public static class PopupPlacement
{
    /// <summary>
    /// Tries below the anchor, then above, right, left; if nothing fits, the popup overlaps the anchor
    /// but always stays inside the work area (the monitor without the taskbar).
    /// </summary>
    public static PixelRect Place(PixelRect anchor, PixelRect workArea, int width, int height, int gap = 0)
    {
        width = Math.Min(width, workArea.Width);
        height = Math.Min(height, workArea.Height);

        int alignedX = Clamp(anchor.X, workArea.X, workArea.Right - width);
        int alignedY = Clamp(anchor.Y, workArea.Y, workArea.Bottom - height);

        if (anchor.Bottom + gap + height <= workArea.Bottom)
            return new PixelRect(alignedX, Math.Max(anchor.Bottom + gap, workArea.Y), width, height);
        if (anchor.Y - gap - height >= workArea.Y)
            return new PixelRect(alignedX, Math.Min(anchor.Y - gap - height, workArea.Bottom - height), width, height);
        if (anchor.Right + gap + width <= workArea.Right)
            return new PixelRect(Math.Max(anchor.Right + gap, workArea.X), alignedY, width, height);
        if (anchor.X - gap - width >= workArea.X)
            return new PixelRect(Math.Min(anchor.X - gap - width, workArea.Right - width), alignedY, width, height);

        // No free side: cover the lower part of the region, still inside the work area.
        return new PixelRect(alignedX, workArea.Bottom - height, width, height);
    }

    private static int Clamp(int value, int min, int max) => max < min ? min : Math.Clamp(value, min, max);
}
