using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using CursorJail.Native;

namespace CursorJail;

// No ClipCursor call, foreground activation, visible windows, hotkeys or persisted settings.
internal static class SmokeTest
{
    internal static void Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var results = new List<string>();
        var window = new MainWindow(smoke: true);
        nint panel = new WindowInteropHelper(window).EnsureHandle();
        Check(NativeMethods.IsWindow(panel), "WPF control panel creates a native HWND", results);
        Check(NativeMethods.AreDpiAwarenessContextsEqual(NativeMethods.GetThreadDpiAwarenessContext(), -4), "PerMonitorV2 DPI awareness is active", results);
        Check(WindowsPlatform.Monitors().Count > 0, "Physical monitor enumeration", results);
        Check(NativeMethods.GetClientRect(panel, out var client) && client.Right > 0, "Native client coordinates", results);
        var overlay = window.Overlay;
        nint handle = overlay.Handle;
        overlay.SetRegion(window.Region, WindowsPlatform.DesktopBounds(WindowsPlatform.Monitors()));
        Check(NativeMethods.GetWindowRect(handle, out var actual) && actual.ToScreenRect() == window.Region,
            "Overlay HWND bounds equal physical cage coordinates", results);
        long styles = (long)NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle);
        Check((styles & NativeMethods.Transparent) != 0 && (styles & NativeMethods.NoActivate) != 0 && (styles & NativeMethods.ToolWindow) != 0,
            "Locked overlay is click-through, non-activating and excluded from taskbar", results);
        Check((styles & 0x80000) != 0, "Overlay has WS_EX_LAYERED", results);
        overlay.SetMode(editing: true, visible: false);
        styles = (long)NativeMethods.GetWindowLongPtr(handle, NativeMethods.GwlExStyle);
        Check((styles & NativeMethods.Transparent) == 0, "Edit mode accepts mouse input", results);
        overlay.SetMode(editing: false, visible: false);
        NativeMethods.WinEventProc callback = (_, _, _, _, _, _, _) => { };
        nint hook = NativeMethods.SetWinEventHook(3, 3, 0, callback, 0, 0, 0);
        Check(hook != 0, "Passive foreground event hook registers", results);
        NativeMethods.UnhookWinEvent(hook);
        GC.KeepAlive(callback);
        using (var shortcuts = new GlobalHotkeys(panel))
        {
            var testKey = new Hotkey(6, 0x87); // Ctrl + Shift + F24, temporary and never dispatched.
            Check(shortcuts.SetToggle(testKey), "Native toggle shortcut registers", results);
            Check(shortcuts.SetToggle(testKey), "Reapplying the same shortcut preserves its binding", results);
            Check(shortcuts.SetToggle(new Hotkey(3, 0x87)), "Native toggle shortcut can be replaced", results);
            results.Add(shortcuts.Ready ? "PASS Emergency shortcut registers" : "NOTICE Emergency shortcut already reserved; app correctly disables locking");
        }
        using var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/CursorJail.ico"))!.Stream;
        using var icon = new System.Drawing.Icon(iconStream);
        Check(icon.Width > 0, "Embedded tray icon loads", results);
        var picker = (ComboBox)window.FindName("WindowPicker");
        picker.ItemsSource = new[] { "Choose a game or open window" };
        picker.SelectedIndex = 0;
        ((Button)window.FindName("LockButton")).IsEnabled = true; // Presentation only; no input loop is run.
        // Render without presenting the panel or overlay to the input desktop.
        Render(window, Path.Combine(outputDirectory, "CursorJail-panel.png"), 620, 930, 1);
        Render(window, Path.Combine(outputDirectory, "CursorJail-panel-150.png"), 620, 930, 1.5);
        Render(window, Path.Combine(outputDirectory, "CursorJail-panel-compact.png"), 570, 600, 1);
        results.Add("Control panel rendered at 100%, 150%, and compact height");
        File.WriteAllText(Path.Combine(outputDirectory, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        window.Close();
    }
    private static void Check(bool condition, string name, List<string> results)
    {
        if (!condition) throw new InvalidOperationException(name);
        results.Add("PASS " + name);
    }
    private static void Render(Window window, string path, double width, double height, double scale)
    {
        // A hidden Window has no rendered pixels. Detach and render its visible content tree.
        var root = (FrameworkElement)window.Content;
        window.Content = null;
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)(width * scale), (int)(height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(root);
        var pixels = new int[bitmap.PixelWidth * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, bitmap.PixelWidth * 4, 0);
        if (pixels.All(p => p == 0)) throw new InvalidOperationException("Control panel rendered no pixels.");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
        window.Content = root;
    }
}
