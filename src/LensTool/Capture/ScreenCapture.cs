using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LensTool.Interop;

namespace LensTool.Capture;

/// <summary>A snapshot of the whole virtual screen (all monitors) in physical pixels.</summary>
public sealed class ScreenSnapshot(BitmapSource image, PixelRect bounds, IReadOnlyList<MonitorInfo> monitors)
{
    public BitmapSource Image { get; } = image;

    /// <summary>Virtual screen bounds; image pixel (0,0) corresponds to (Bounds.X, Bounds.Y).</summary>
    public PixelRect Bounds { get; } = bounds;

    public IReadOnlyList<MonitorInfo> Monitors { get; } = monitors;

    /// <summary>Crops a region given in screen coordinates.</summary>
    public BitmapSource Crop(PixelRect screenRect)
    {
        var r = screenRect.Intersect(Bounds);
        if (r.IsEmpty)
            throw new ArgumentException($"Область {screenRect} вне экрана {Bounds}.", nameof(screenRect));

        var cropped = new CroppedBitmap(Image, new Int32Rect(r.X - Bounds.X, r.Y - Bounds.Y, r.Width, r.Height));
        cropped.Freeze();
        return cropped;
    }
}

public static class ScreenCapture
{
    public static ScreenSnapshot CaptureVirtualScreen()
    {
        var bounds = new PixelRect(
            NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN),
            NativeMethods.GetSystemMetrics(NativeMethods.SM_CYVIRTUALSCREEN));

        return new ScreenSnapshot(CaptureRect(bounds), bounds, MonitorInfo.GetAll());
    }

    /// <summary>Copies screen pixels via GDI (BitBlt) into a 32-bit BitmapSource.</summary>
    public static BitmapSource CaptureRect(PixelRect r)
    {
        IntPtr screenDc = NativeMethods.GetDC(IntPtr.Zero);
        IntPtr memDc = NativeMethods.CreateCompatibleDC(screenDc);
        IntPtr hBitmap = NativeMethods.CreateCompatibleBitmap(screenDc, r.Width, r.Height);
        try
        {
            IntPtr old = NativeMethods.SelectObject(memDc, hBitmap);
            // CAPTUREBLT also captures layered (semi-transparent) windows.
            bool ok = NativeMethods.BitBlt(memDc, 0, 0, r.Width, r.Height, screenDc, r.X, r.Y,
                NativeMethods.SRCCOPY | NativeMethods.CAPTUREBLT);
            NativeMethods.SelectObject(memDc, old);
            if (!ok)
                throw new InvalidOperationException($"BitBlt не удался (код {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}).");

            var bmi = new NativeMethods.BITMAPINFO
            {
                bmiHeader = new NativeMethods.BITMAPINFOHEADER
                {
                    biSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                    biWidth = r.Width,
                    biHeight = -r.Height, // negative height = top-down rows
                    biPlanes = 1,
                    biBitCount = 32,
                    biCompression = NativeMethods.BI_RGB,
                },
            };

            int stride = r.Width * 4;
            var pixels = new byte[stride * r.Height];
            if (NativeMethods.GetDIBits(memDc, hBitmap, 0, (uint)r.Height, pixels, ref bmi, NativeMethods.DIB_RGB_COLORS) == 0)
                throw new InvalidOperationException("GetDIBits не удался.");

            // Bgr32: the alpha channel is ignored (GDI leaves garbage/zeros there).
            var image = BitmapSource.Create(r.Width, r.Height, 96, 96, PixelFormats.Bgr32, null, pixels, stride);
            image.Freeze();
            return image;
        }
        finally
        {
            NativeMethods.DeleteObject(hBitmap);
            NativeMethods.DeleteDC(memDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
