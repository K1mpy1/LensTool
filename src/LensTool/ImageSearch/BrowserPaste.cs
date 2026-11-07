using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using LensTool.Interop;

namespace LensTool.ImageSearch;

/// <summary>
/// Presses Ctrl+V in the browser once the Google page opened by the app is in front.
/// Keys are sent only to a window titled exactly "Google" (plus the browser name), so they never
/// land in another app or in an older Google results tab.
/// </summary>
public static partial class BrowserPaste
{
    private static readonly TimeSpan WaitForPage = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(200);
    // The Lens dialog needs a moment after the title appears before it accepts a paste.
    private static readonly TimeSpan DialogReady = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan WaitForSearch = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Waits for the Google page, pastes, and checks that the page moved on to the results.
    /// False when the page did not appear or the paste did not start a search.
    /// </summary>
    public static async Task<bool> PasteIntoGoogleAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        IntPtr window = IntPtr.Zero;
        while (sw.Elapsed < WaitForPage)
        {
            if (IsGoogleHomeTitle(ForegroundTitle(out var hwnd)))
            {
                window = hwnd;
                break;
            }
            await Task.Delay(PollInterval, cancellationToken);
        }
        if (window == IntPtr.Zero)
        {
            AppLog.Info($"Автовставка: окно Google не стало активным за {WaitForPage.TotalSeconds:0} с");
            return false;
        }

        await Task.Delay(DialogReady, cancellationToken);
        if (NativeMethods.GetForegroundWindow() != window)
        {
            AppLog.Info("Автовставка: пользователь переключился на другое окно, Ctrl+V не нажимаю");
            return false;
        }

        PressCtrlV();
        AppLog.Info($"Автовставка: Ctrl+V через {sw.ElapsedMilliseconds} мс после открытия");

        // A successful paste starts a Lens search and the title changes to the results page.
        var searchWait = Stopwatch.StartNew();
        while (searchWait.Elapsed < WaitForSearch)
        {
            await Task.Delay(PollInterval, cancellationToken);
            if (!IsGoogleHomeTitle(ForegroundTitle(out _)))
                return true;
        }
        AppLog.Info("Автовставка: страница не перешла к результатам");
        return false;
    }

    /// <summary>"Google", "Google — Zen Browser", "Google - Opera": the Google home page with the Lens dialog.</summary>
    public static bool IsGoogleHomeTitle(string title) => GoogleHomeTitle().IsMatch(title);

    private static string ForegroundTitle(out IntPtr hwnd)
    {
        hwnd = NativeMethods.GetForegroundWindow();
        var sb = new StringBuilder(512);
        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static void PressCtrlV()
    {
        NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, 0, IntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_V, 0, 0, IntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_V, 0, NativeMethods.KEYEVENTF_KEYUP, IntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_CONTROL, 0, NativeMethods.KEYEVENTF_KEYUP, IntPtr.Zero);
    }

    [GeneratedRegex(@"^Google(\s+[-—–]\s+.*)?$")]
    private static partial Regex GoogleHomeTitle();
}
