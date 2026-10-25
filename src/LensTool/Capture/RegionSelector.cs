using LensTool.Interop;
using LensTool.UI;

namespace LensTool.Capture;

/// <summary>
/// Region selection session: captures all monitors, shows an overlay window on each of them
/// and tracks the selection in shared physical coordinates, so the frame can be dragged across
/// monitors with different DPI.
/// </summary>
public sealed class RegionSelector
{
    private const int MinSize = 3;

    private readonly List<SelectionOverlayWindow> _windows = [];
    private TaskCompletionSource<CaptureResult?>? _completion;
    private ScreenSnapshot? _snapshot;
    private (int X, int Y)? _dragStart;

    public bool IsActive => _completion is not null;

    /// <summary>Shows the selection UI and waits for the result; null means the user cancelled.</summary>
    public async Task<CaptureResult?> SelectAsync(string hint)
    {
        if (_completion is not null)
            throw new InvalidOperationException("Выделение уже идёт.");

        _completion = new TaskCompletionSource<CaptureResult?>();
        try
        {
            _snapshot = ScreenCapture.CaptureVirtualScreen();
            AppLog.Info($"Снимок экрана {_snapshot.Bounds}, мониторов: {_snapshot.Monitors.Count}");

            var cursor = GetCursor();
            SelectionOverlayWindow? active = null;
            foreach (var monitor in _snapshot.Monitors)
            {
                bool underCursor = monitor.Bounds.Contains(cursor.X, cursor.Y);
                var window = new SelectionOverlayWindow(monitor, _snapshot.Crop(monitor.Bounds), underCursor ? hint : null);
                window.DragStarted += OnDragStarted;
                window.DragMoved += OnDragMoved;
                window.DragEnded += OnDragEnded;
                window.Cancelled += Cancel;
                _windows.Add(window);
                window.Show();
                if (underCursor)
                    active = window;
            }

            // Focus the window under the cursor so that Esc works.
            active ??= _windows.FirstOrDefault();
            if (active is not null)
            {
                active.Activate();
                NativeMethods.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(active).Handle);
            }

            return await _completion.Task;
        }
        finally
        {
            foreach (var window in _windows)
                window.CloseByOwner();
            _windows.Clear();
            _snapshot = null;
            _dragStart = null;
            _completion = null;
        }
    }

    public void Cancel() => _completion?.TrySetResult(null);

    private void OnDragStarted()
    {
        _dragStart = GetCursor();
        UpdateWindows(null);
    }

    private void OnDragMoved()
    {
        if (_dragStart is null)
            return;
        UpdateWindows(CurrentRect());
    }

    private void OnDragEnded()
    {
        if (_dragStart is null || _snapshot is null)
            return;

        var rect = CurrentRect().Intersect(_snapshot.Bounds);
        _dragStart = null;
        if (rect.Width < MinSize || rect.Height < MinSize)
        {
            // Just a click: reset and wait for a new selection.
            UpdateWindows(null);
            return;
        }

        var monitor = FindMonitor(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        AppLog.Info($"Выделено {rect} на {monitor.DeviceName} (DPI {monitor.Dpi})");
        _completion?.TrySetResult(new CaptureResult(rect, _snapshot.Crop(rect), monitor));
    }

    private PixelRect CurrentRect()
    {
        var start = _dragStart!.Value;
        var cur = GetCursor();
        return PixelRect.FromPoints(start.X, start.Y, cur.X, cur.Y);
    }

    private void UpdateWindows(PixelRect? rect)
    {
        foreach (var window in _windows)
            window.SetSelection(rect);
    }

    private MonitorInfo FindMonitor(int x, int y) =>
        _snapshot!.Monitors.FirstOrDefault(m => m.Bounds.Contains(x, y))
        ?? _snapshot.Monitors.FirstOrDefault(m => m.IsPrimary)
        ?? _snapshot.Monitors[0];

    /// <summary>Cursor position in physical pixels: a single coordinate system for all monitors.</summary>
    private static (int X, int Y) GetCursor()
    {
        NativeMethods.GetCursorPos(out var p);
        return (p.X, p.Y);
    }
}
