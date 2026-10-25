using System.Windows;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace LensTool.UI;

/// <summary>Tray icon with a context menu and balloon notifications.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _notifyIcon;
    private readonly Drawing.Icon _icon;
    private Action? _balloonClick;

    public TrayIcon(string tooltip)
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"))
            ?? throw new InvalidOperationException("Не найдена иконка Assets/app.ico.");
        using (var stream = resource.Stream)
            _icon = new Drawing.Icon(stream, Forms.SystemInformation.SmallIconSize);

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = tooltip,
            ContextMenuStrip = new Forms.ContextMenuStrip(),
            Visible = true,
        };
        _notifyIcon.BalloonTipClicked += (_, _) => _balloonClick?.Invoke();
        _notifyIcon.BalloonTipClosed += (_, _) => _balloonClick = null;
    }

    public void AddMenuItem(string text, Action onClick, string? shortcut = null, bool bold = false)
    {
        var item = new Forms.ToolStripMenuItem(text) { ShortcutKeyDisplayString = shortcut };
        if (bold)
            item.Font = new Drawing.Font(item.Font, Drawing.FontStyle.Bold);
        item.Click += (_, _) => onClick();
        _notifyIcon.ContextMenuStrip!.Items.Add(item);
    }

    public void AddSeparator() => _notifyIcon.ContextMenuStrip!.Items.Add(new Forms.ToolStripSeparator());

    public void ShowBalloon(string title, string text, bool isError = false, Action? onClick = null)
    {
        _balloonClick = onClick;
        _notifyIcon.ShowBalloonTip(5000, title, text, isError ? Forms.ToolTipIcon.Error : Forms.ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _icon.Dispose();
    }
}
