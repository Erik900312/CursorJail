using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;

namespace CursorJail.Native;

internal sealed record WindowItem(nint Handle, string Title, ImageSource? Icon = null)
{
    public override string ToString() => Title;
}
internal sealed record MonitorItem(nint Handle, string Name, ScreenRect Bounds, bool Primary)
{
    public override string ToString() => $"{Name.Replace("\\\\.\\", "")} · {Bounds.Width:N0} × {Bounds.Height:N0}";
}

internal sealed class WindowsPlatform : IAttachmentPlatform
{
    public PassKey PassKey { get; set; }
    public bool TrimWindow { get; set; } = true;
    internal Func<ScreenRect, bool>? RegionAvailable { get; set; }
    public nint ForegroundWindow => NativeMethods.GetAncestor(NativeMethods.GetForegroundWindow(), 2);
    internal static bool Down(int key) => (NativeMethods.GetAsyncKeyState(key) & 0x8000) != 0;
    public bool IsPassKeyDown => Down(PassKey switch { PassKey.LeftAlt => 0xA4, PassKey.MouseBack => 0x05, PassKey.MouseForward => 0x06, _ => 0x04 });
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
            if (title.Length > 0) windows.Add(new(window, title.ToString(), WindowIcon(window)));
            return true;
        }, 0);
        return windows.OrderBy(w => w.Title, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public bool TryIdentifyWindow(nint handle, out AttachedWindow window)
    {
        window = null!;
        if (!IsUsableWindow(handle)) return false;
        NativeMethods.GetWindowThreadProcessId(handle, out uint processId);
        using var process = NativeMethods.OpenProcess(0x1000, false, processId);
        if (process.IsInvalid) return false;
        var path = new StringBuilder(32768);
        int capacity = path.Capacity;
        if (!NativeMethods.QueryFullProcessImageName(process, 0, path, ref capacity)) return false;
        var windowClass = new StringBuilder(256);
        if (NativeMethods.GetClassName(handle, windowClass, windowClass.Capacity) == 0) return false;
        string rawClass = windowClass.ToString();
        var identity = new AppIdentity(path.ToString(), IdentityWindowClass(rawClass));
        if (!identity.IsValid) return false;
        window = new(handle, processId, identity, rawClass);
        return true;
    }
    public bool IsWindowInstance(AttachedWindow window)
    {
        if (!NativeMethods.IsWindow(window.Handle)) return false;
        NativeMethods.GetWindowThreadProcessId(window.Handle, out uint processId);
        if (processId != window.ProcessId) return false;
        if (window.InstanceClass is null) return true;
        var windowClass = new StringBuilder(256);
        return NativeMethods.GetClassName(window.Handle, windowClass, windowClass.Capacity) != 0
            && windowClass.ToString().Equals(window.InstanceClass, StringComparison.Ordinal);
    }
    public bool TryClientRegion(AttachedWindow window, out ScreenRect client)
    {
        client = default;
        return IsWindowInstance(window) && IsUsableWindow(window.Handle) && TryWindowRegion(window.Handle, trim: true, out client);
    }
    public bool IsRegionAvailable(ScreenRect region) => RegionAvailable?.Invoke(region) == true;

    internal static string IdentityWindowClass(string raw)
    {
        // WPF appends a per-window GUID. Keep its application/domain prefix for relaunch matching.
        int separator = raw.IndexOf(';');
        return raw.StartsWith("HwndWrapper[", StringComparison.Ordinal) && separator > 0 ? raw[..separator] + ";]" : raw;
    }

    internal static ImageSource WindowIcon(nint handle)
    {
        nint icon = 0;
        if (handle != 0)
        {
            if (NativeMethods.SendMessageTimeout(handle, 0x7F, 2, 0, 2, 50, out var result) != 0) icon = (nint)result;
            if (icon == 0) icon = NativeMethods.GetClassLongPtr(handle, -34);
            if (icon == 0) icon = NativeMethods.GetClassLongPtr(handle, -14);
        }
        if (icon == 0) icon = System.Drawing.SystemIcons.Application.Handle;
        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(icon, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(20, 20));
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is ArgumentException or System.ComponentModel.Win32Exception or System.Runtime.InteropServices.COMException)
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(System.Drawing.SystemIcons.Application.Handle, Int32Rect.Empty, System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(20, 20));
            image.Freeze();
            return image;
        }
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
