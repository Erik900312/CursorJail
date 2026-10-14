using System.Runtime.InteropServices;
using System.Text;

namespace CursorJail.Native;

internal sealed record WindowItem(nint Handle, string Title)
{
    public override string ToString() => Title;
}
internal sealed record MonitorItem(nint Handle, string Name, ScreenRect Bounds, bool Primary)
{
    public override string ToString() => $"{Name.Replace("\\\\.\\", "")} · {Bounds.Width:N0} × {Bounds.Height:N0}";
}

internal sealed class WindowsPlatform : ICursorPlatform
{
    public PassKey PassKey { get; set; }
    public bool TrimWindow { get; set; } = true;
    public nint ForegroundWindow => NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), 2);
    internal static bool Down(int key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
    public bool IsPassKeyDown => Down(PassKey == PassKey.LeftAlt ? 0xA4 : 0x04);
    public bool IsEscapeChordDown => Down(0x5B) || Down(0x5C) || (Down(0x12) && Down(0x09)) || (Down(0x11) && Down(0x10) && Down(0x1B));

    public bool IsUsableWindow(nint window)
    {
        if (window == 0 || !NativeMethods.IsWindow(window) || !NativeMethods.IsWindowVisible(window) || NativeMethods.IsIconic(window)) return false;
        NativeMethods.GetWindowThreadProcessId(window, out uint process);
        if (process == Environment.ProcessId) return false;
        var name = new StringBuilder(128);
        NativeMethods.GetClassName(window, name, name.Capacity);
        return name.ToString() is not ("Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd");
    }

    public bool TryConfine(ScreenRect region)
    {
        var rect = new NativeMethods.Rect(region);
        return NativeMethods.ClipCursor(ref rect);
    }
    public bool TryRelease() => NativeMethods.ReleaseCursor(0);
    public bool TryGetClip(out ScreenRect region)
    {
        bool ok = NativeMethods.GetClipCursor(out var rect);
        region = rect.ToScreenRect();
        return ok;
    }
    public bool TryGetTargetRegion(nint window, out ScreenRect region) => TryWindowRegion(window, TrimWindow, out region);

    internal static bool TryWindowRegion(nint window, bool trim, out ScreenRect region)
    {
        region = default;
        if (!NativeMethods.IsWindow(window) || NativeMethods.IsIconic(window)) return false;
        if (trim)
        {
            if (!NativeMethods.GetClientRect(window, out var client)) return false;
            var origin = new NativeMethods.Point();
            if (!NativeMethods.ClientToScreen(window, ref origin)) return false;
            region = new(origin.X, origin.Y, origin.X + client.Right, origin.Y + client.Bottom);
        }
        else
        {
            if (NativeMethods.DwmGetWindowAttribute(window, 9, out var frame, Marshal.SizeOf<NativeMethods.Rect>()) != 0
                && !NativeMethods.GetWindowRect(window, out frame)) return false;
            region = frame.ToScreenRect();
        }
        return region.IsValid;
    }

    internal IReadOnlyList<WindowItem> Windows()
    {
        var windows = new List<WindowItem>();
        NativeMethods.EnumWindows((window, _) =>
        {
            if (!IsUsableWindow(window)) return true;
            if (((long)NativeMethods.GetWindowLongPtr(window, NativeMethods.GwlExStyle) & NativeMethods.ToolWindow) != 0) return true;
            var title = new StringBuilder(512);
            NativeMethods.GetWindowText(window, title, title.Capacity);
            if (title.Length > 0) windows.Add(new(window, title.ToString()));
            return true;
        }, 0);
        return windows.OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    internal static IReadOnlyList<MonitorItem> Monitors()
    {
        var monitors = new List<MonitorItem>();
        NativeMethods.EnumDisplayMonitors(0, 0, (nint handle, nint _, ref NativeMethods.Rect rect, nint data) =>
        {
            var info = new NativeMethods.MonitorInfo { Size = Marshal.SizeOf<NativeMethods.MonitorInfo>(), Device = "" };
            if (NativeMethods.GetMonitorInfo(handle, ref info)) monitors.Add(new(handle, info.Device, info.Monitor.ToScreenRect(), (info.Flags & 1) != 0));
            return true;
        }, 0);
        return monitors.OrderByDescending(m => m.Primary).ThenBy(m => m.Bounds.Left).ToList();
    }

    internal static ScreenRect DesktopBounds(IReadOnlyList<MonitorItem> monitors) =>
        new(monitors.Min(m => m.Bounds.Left), monitors.Min(m => m.Bounds.Top), monitors.Max(m => m.Bounds.Right), monitors.Max(m => m.Bounds.Bottom));
}
