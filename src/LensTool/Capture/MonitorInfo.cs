using LensTool.Interop;

namespace LensTool.Capture;

/// <summary>A monitor: bounds in physical pixels and its DPI.</summary>
public sealed record MonitorInfo(string DeviceName, PixelRect Bounds, PixelRect WorkArea, bool IsPrimary, uint Dpi)
{
    public double Scale => Dpi / 96.0;

    public static IReadOnlyList<MonitorInfo> GetAll()
    {
        var result = new List<MonitorInfo>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr _, ref NativeMethods.RECT _, IntPtr _) =>
        {
            var info = new NativeMethods.MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
            if (!NativeMethods.GetMonitorInfo(hMonitor, ref info))
                return true;

            uint dpi = NativeMethods.GetDpiForMonitor(hMonitor, NativeMethods.MDT_EFFECTIVE_DPI, out var dpiX, out _) == 0 ? dpiX : 96;
            result.Add(new MonitorInfo(
                info.szDevice,
                ToPixelRect(info.rcMonitor),
                ToPixelRect(info.rcWork),
                (info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
                dpi));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    private static PixelRect ToPixelRect(NativeMethods.RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
}
