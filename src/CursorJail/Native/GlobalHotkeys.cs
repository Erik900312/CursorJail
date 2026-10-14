using System.ComponentModel;
using System.Windows.Interop;

namespace CursorJail.Native;

internal sealed class GlobalHotkeys : IDisposable
{
    private readonly HwndSource _source;
    private readonly nint _window;
    private Hotkey? _currentHotkey;
    private bool _disposed;
    private int _toggleId = 101;
    private bool _toggleRegistered;
    private bool _emergencyRegistered;
    private bool _editRegistered;
    public bool Ready => _toggleRegistered && _emergencyRegistered;
    public event Action? Toggle;
    public event Action? Emergency;
    public event Action? Edit;
    internal bool EditReady => _editRegistered;
    public string? Error { get; private set; }

    internal GlobalHotkeys(nint window)
    {
        _window = window;
        _source = HwndSource.FromHwnd(window)!;
        _source.AddHook(Hook);
        _emergencyRegistered = NativeMethods.RegisterHotKey(window, 100, Hotkey.Emergency.Modifiers | 0x4000, Hotkey.Emergency.Key);
        if (!_emergencyRegistered) Error = "Emergency shortcut Ctrl + Alt + Esc is used by another app. Close that app before locking.";
        _editRegistered = NativeMethods.RegisterHotKey(window, 103, 3 | 0x4000, 0x45);
    }

    internal bool SetToggle(Hotkey hotkey)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!hotkey.IsValid || hotkey == Hotkey.Emergency) { Error = "Choose another shortcut. Ctrl + Alt + Esc is reserved for release."; return false; }
        if (_toggleRegistered && _currentHotkey == hotkey)
        {
            RetryEmergency();
            return true;
        }
        int candidate = _toggleId == 101 ? 102 : 101;
        bool relinquishedEditor = hotkey == new Hotkey(3, 0x45) && _editRegistered;
        if (relinquishedEditor) { NativeMethods.UnregisterHotKey(_window, 103); _editRegistered = false; }
        if (!NativeMethods.RegisterHotKey(_window, candidate, hotkey.Modifiers | 0x4000, hotkey.Key))
        {
            string reason = new Win32Exception().Message;
            if (relinquishedEditor) TryRegisterEditor();
            Error = "That shortcut is unavailable. " + reason;
            return false;
        }
        if (_toggleRegistered) NativeMethods.UnregisterHotKey(_window, _toggleId);
        _toggleId = candidate;
        _toggleRegistered = true;
        _currentHotkey = hotkey;
        TryRegisterEditor();
        RetryEmergency();
        return true;
    }

    private void RetryEmergency()
    {
        if (!_emergencyRegistered)
            _emergencyRegistered = NativeMethods.RegisterHotKey(_window, 100, Hotkey.Emergency.Modifiers | 0x4000, Hotkey.Emergency.Key);
        Error = _emergencyRegistered ? null : "Ctrl + Alt + Esc is unavailable. Locking is disabled until it can be registered.";
    }

    internal static string Label(Hotkey hotkey)
    {
        var parts = new List<string>();
        if ((hotkey.Modifiers & 2) != 0) parts.Add("Ctrl");
        if ((hotkey.Modifiers & 1) != 0) parts.Add("Alt");
        if ((hotkey.Modifiers & 4) != 0) parts.Add("Shift");
        if ((hotkey.Modifiers & 8) != 0) parts.Add("Win");
        parts.Add(hotkey.Key == 0x91 ? "Scroll Lock" : KeyInterop.KeyFromVirtualKey((int)hotkey.Key).ToString());
        return string.Join(" + ", parts);
    }

    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message != NativeMethods.WmHotkey) return 0;
        if ((int)wParam == 100) { Emergency?.Invoke(); handled = true; }
        else if ((int)wParam == 103) { Edit?.Invoke(); handled = true; }
        else if ((int)wParam == _toggleId) { Toggle?.Invoke(); handled = true; }
        return 0;
    }

    private void TryRegisterEditor()
    {
        if (!_editRegistered && _currentHotkey != new Hotkey(3, 0x45))
            _editRegistered = NativeMethods.RegisterHotKey(_window, 103, 3 | 0x4000, 0x45);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_toggleRegistered) NativeMethods.UnregisterHotKey(_window, _toggleId);
        if (_emergencyRegistered) NativeMethods.UnregisterHotKey(_window, 100);
        if (_editRegistered) NativeMethods.UnregisterHotKey(_window, 103);
        if (!_source.IsDisposed) _source.RemoveHook(Hook);
        _toggleRegistered = _emergencyRegistered = false;
    }
}
