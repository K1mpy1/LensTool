using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using LensTool.Interop;
using LensTool.Settings;

namespace LensTool.UI;

/// <summary>Global hotkeys via RegisterHotKey on a hidden message-only window.</summary>
public sealed class GlobalHotkeys : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = [];
    private int _nextId = 1;

    public GlobalHotkeys()
    {
        _source = new HwndSource(new HwndSourceParameters("LensTool.Hotkeys")
        {
            ParentWindow = NativeMethods.HWND_MESSAGE,
            WindowStyle = 0,
        });
        _source.AddHook(WndProc);
    }

    /// <summary>Registers a key combination. Returns false if another app already owns it.</summary>
    public bool TryRegister(HotkeyGesture gesture, Action handler)
    {
        int id = _nextId++;
        uint modifiers = NativeMethods.MOD_NOREPEAT;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= NativeMethods.MOD_ALT;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= NativeMethods.MOD_CONTROL;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= NativeMethods.MOD_SHIFT;
        if (gesture.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= NativeMethods.MOD_WIN;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(gesture.Key);

        if (!NativeMethods.RegisterHotKey(_source.Handle, id, modifiers, vk))
        {
            AppLog.Error($"Не удалось зарегистрировать {gesture} (код {Marshal.GetLastWin32Error()})");
            return false;
        }

        _handlers[id] = handler;
        AppLog.Info($"Горячая клавиша {gesture} зарегистрирована");
        return true;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            handled = true;
            handler();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (int id in _handlers.Keys)
            NativeMethods.UnregisterHotKey(_source.Handle, id);
        _handlers.Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
