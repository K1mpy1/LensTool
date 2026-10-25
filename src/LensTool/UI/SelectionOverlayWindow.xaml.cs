using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using LensTool.Capture;
using LensTool.Interop;

namespace LensTool.UI;

/// <summary>
/// Full-screen selection window for a single monitor. It makes no decisions itself:
/// it reports mouse/keyboard actions and draws the frame as told by <see cref="RegionSelector"/>.
/// </summary>
public partial class SelectionOverlayWindow : Window
{
    private readonly MonitorInfo _monitor;
    private bool _closingByOwner;
    private PixelRect? _selection;

    public event Action? DragStarted;
    public event Action? DragMoved;
    public event Action? DragEnded;
    public event Action? Cancelled;

    public SelectionOverlayWindow(MonitorInfo monitor, BitmapSource monitorImage, string? hint)
    {
        InitializeComponent();
        _monitor = monitor;
        BackgroundImage.Source = monitorImage;
        if (hint is not null)
        {
            HintText.Text = hint;
            HintPanel.Visibility = Visibility.Visible;
        }

        SourceInitialized += (_, _) => PlaceOnMonitor();
        Loaded += (_, _) =>
        {
            PlaceOnMonitor();
            LogPlacement();
        };
        // If the window lands on a monitor with a different DPI, WPF resizes it for the new scale,
        // so restore the exact physical bounds of the monitor.
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(PlaceOnMonitor);
        SizeChanged += (_, _) => Redraw();
    }

    public MonitorInfo Monitor => _monitor;

    /// <summary>Shows the selection (in physical screen pixels) or clears it (null).</summary>
    public void SetSelection(PixelRect? selection)
    {
        _selection = selection;
        if (selection is not null)
            HintPanel.Visibility = Visibility.Collapsed;
        Redraw();
    }

    public void CloseByOwner()
    {
        _closingByOwner = true;
        Close();
    }

    private void PlaceOnMonitor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
            return;

        var b = _monitor.Bounds;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, NativeMethods.SWP_NOACTIVATE);
    }

    private void LogPlacement()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.GetWindowRect(hwnd, out var r);
        var dpi = VisualTreeHelper.GetDpi(this);
        AppLog.Info($"Оверлей {_monitor.DeviceName}: монитор {_monitor.Bounds} DPI {_monitor.Dpi}, " +
                    $"окно {r.Left},{r.Top} {r.Right - r.Left}x{r.Bottom - r.Top}, WPF scale {dpi.DpiScaleX:0.##}, " +
                    $"размер {ActualWidth:0.#}x{ActualHeight:0.#} DIP");
    }

    private void Redraw()
    {
        var full = new Rect(RenderSize);
        if (_selection is not { IsEmpty: false } sel || PresentationSource.FromVisual(this) is null)
        {
            DimPath.Data = new RectangleGeometry(full);
            SelectionBorder.Visibility = Visibility.Collapsed;
            SizeLabel.Visibility = Visibility.Collapsed;
            return;
        }

        // PointFromScreen converts physical pixels to this window's DIPs using its DPI.
        var local = new Rect(PointFromScreen(new Point(sel.X, sel.Y)), PointFromScreen(new Point(sel.Right, sel.Bottom)));

        var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
        geometry.Children.Add(new RectangleGeometry(full));
        geometry.Children.Add(new RectangleGeometry(local));
        DimPath.Data = geometry;

        SelectionBorder.Width = local.Width;
        SelectionBorder.Height = local.Height;
        System.Windows.Controls.Canvas.SetLeft(SelectionBorder, local.X);
        System.Windows.Controls.Canvas.SetTop(SelectionBorder, local.Y);
        SelectionBorder.Visibility = Visibility.Visible;

        // The size label is shown only on the monitor containing the selection's top-left corner.
        if (_monitor.Bounds.Contains(sel.X, sel.Y))
        {
            SizeText.Text = $"{sel.Width} × {sel.Height}";
            SizeLabel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            double labelTop = local.Y - SizeLabel.DesiredSize.Height - 4;
            System.Windows.Controls.Canvas.SetLeft(SizeLabel, local.X);
            System.Windows.Controls.Canvas.SetTop(SizeLabel, labelTop >= 0 ? labelTop : local.Y + 4);
            SizeLabel.Visibility = Visibility.Visible;
        }
        else
        {
            SizeLabel.Visibility = Visibility.Collapsed;
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        CaptureMouse();
        DragStarted?.Invoke();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMoved?.Invoke();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();
        DragEnded?.Invoke();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        Cancelled?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
            Cancelled?.Invoke();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        // Alt+F4 etc. counts as cancel; only RegionSelector closes these windows.
        if (!_closingByOwner)
        {
            e.Cancel = true;
            Cancelled?.Invoke();
        }
    }
}
