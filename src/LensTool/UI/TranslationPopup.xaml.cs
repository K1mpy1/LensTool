using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using LensTool.Capture;
using LensTool.Interop;

namespace LensTool.UI;

/// <summary>
/// Card with the translation shown next to the selected region. Closes on Esc, ✕ or a click elsewhere.
/// </summary>
public partial class TranslationPopup : Window
{
    private const double MinWidthDip = 360;
    private const double MaxWidthDip = 640;
    private const int GapPx = 0; // the transparent shadow margin already separates the card from the region

    private static readonly Brush NormalHeader = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF));
    private static readonly Brush ErrorHeader = new SolidColorBrush(Color.FromRgb(0xFF, 0x8A, 0x80));

    private readonly PixelRect _anchor;
    private readonly MonitorInfo _monitor;
    private string? _translation;
    private string? _original;
    private bool _showingOriginal;

    public TranslationPopup(PixelRect anchor, MonitorInfo monitor)
    {
        InitializeComponent();
        _anchor = anchor;
        _monitor = monitor;

        // Sizes are in DIPs of the target monitor: as wide as the region, within reasonable limits.
        double workWidthDip = monitor.WorkArea.Width / monitor.Scale;
        Width = Math.Min(Math.Clamp(anchor.Width / monitor.Scale + 16, MinWidthDip, MaxWidthDip), workWidthDip);
        BodyScroll.MaxHeight = monitor.WorkArea.Height / monitor.Scale * 0.5;

        // Invisible until the first layout pass, so the card never flashes at a temporary position.
        Opacity = 0;
        SourceInitialized += (_, _) => MoveToMonitor();
        Loaded += (_, _) =>
        {
            Reposition();
            Opacity = 1;
        };
        SizeChanged += (_, _) =>
        {
            if (IsLoaded)
                Reposition();
        };
        // Closing deactivates the window, so ignore Deactivated once closing has started.
        Deactivated += (_, _) =>
        {
            if (!IsClosed)
                Close();
        };
        Closing += (_, _) => IsClosed = true;
    }

    /// <summary>True as soon as closing starts; updates after that are ignored.</summary>
    public bool IsClosed { get; private set; }

    public void ShowStatus(string status, string? original = null)
    {
        _original = original;
        SetHeader(status, isError: false);
        SetBody(original is null ? "…" : original, dimmed: true);
        FooterText.Visibility = Visibility.Collapsed;
        CopyButton.IsEnabled = original is not null;
        ToggleButton.Visibility = Visibility.Collapsed;
    }

    public void ShowTranslation(string header, string translation, string original, string? footer)
    {
        _translation = translation;
        _original = original;
        _showingOriginal = false;
        SetHeader(header, isError: false);
        SetBody(translation, dimmed: false);
        SetFooter(footer);
        CopyButton.IsEnabled = true;
        CopyButton.Content = "Копировать";
        ToggleButton.Visibility = Visibility.Visible;
        ToggleButton.Content = "Оригинал";
    }

    /// <summary>Error text; when the original is known it stays available for copying.</summary>
    public void ShowError(string header, string message, string? original = null)
    {
        _translation = null;
        _original = original;
        SetHeader(header, isError: true);
        SetBody(original is null ? message : $"{message}{Environment.NewLine}{Environment.NewLine}{original}", dimmed: false);
        SetFooter(null);
        CopyButton.IsEnabled = original is not null;
        ToggleButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>Text the copy button puts on the clipboard: what is shown, or the original after an error.</summary>
    public string? CopyableText => _showingOriginal || _translation is null ? _original : _translation;

    private void SetHeader(string text, bool isError)
    {
        HeaderText.Text = text;
        HeaderText.Foreground = isError ? ErrorHeader : NormalHeader;
    }

    private void SetBody(string text, bool dimmed)
    {
        BodyText.Text = text;
        BodyText.Opacity = dimmed ? 0.55 : 1;
        BodyScroll.ScrollToTop();
    }

    private void SetFooter(string? text)
    {
        FooterText.Text = text;
        FooterText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if (_translation is null || _original is null)
            return;
        _showingOriginal = !_showingOriginal;
        SetBody(_showingOriginal ? _original : _translation, dimmed: false);
        ToggleButton.Content = _showingOriginal ? "Перевод" : "Оригинал";
    }

    private void OnCopyClick(object sender, RoutedEventArgs e) => CopyAll();

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void CopyAll()
    {
        if (CopyableText is not { Length: > 0 } text)
            return;
        try
        {
            Clipboard.SetText(text);
            CopyButton.Content = "Скопировано";
        }
        catch (System.Runtime.InteropServices.ExternalException ex) // clipboard is locked by another app
        {
            AppLog.Error("Не удалось записать в буфер обмена", ex);
        }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
        // Ctrl+C without a selection copies everything.
        else if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control && BodyText.SelectionLength == 0)
        {
            CopyAll();
            e.Handled = true;
        }
    }

    /// <summary>Puts the window on the target monitor first, so WPF picks up that monitor's DPI before layout.</summary>
    private void MoveToMonitor()
    {
        var wa = _monitor.WorkArea;
        NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, wa.X, wa.Y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>Places the card next to the region using its real size in physical pixels.</summary>
    private void Reposition()
    {
        if (Handle == IntPtr.Zero || !NativeMethods.GetWindowRect(Handle, out var r))
            return;

        var rect = PopupPlacement.Place(_anchor, _monitor.WorkArea, r.Right - r.Left, r.Bottom - r.Top, GapPx);
        NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, rect.X, rect.Y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        AppLog.Info($"Окно перевода: область {_anchor}, окно {rect}, рабочая область {_monitor.WorkArea}");
    }

    private IntPtr Handle => new WindowInteropHelper(this).Handle;
}
