using System.Text.Json;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using CursorJail.Native;

namespace CursorJail;

// Never calls ClipCursor or activates/shows windows. Shortcut registrations are temporary.
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
        Check((styles & NativeMethods.NoActivate) == 0, "Edit mode can take foreground focus", results);
        overlay.SetMode(editing: false, visible: false);
        NativeMethods.WinEventProc callback = (_, _, _, _, _, _, _) => { };
        nint hook = NativeMethods.SetWinEventHook(3, 3, 0, callback, 0, 0, 0);
        Check(hook != 0, "Passive foreground event hook registers", results);
        NativeMethods.UnhookWinEvent(hook);
        hook = NativeMethods.SetWinEventHook(10, 11, 0, callback, 0, 0, 0);
        Check(hook != 0, "Passive window move/resize hook registers", results);
        NativeMethods.UnhookWinEvent(hook);
        GC.KeepAlive(callback);
        using (var shortcuts = new GlobalHotkeys(panel))
        {
            var testKey = new Hotkey(6, 0x87); // Ctrl + Shift + F24, temporary and never dispatched.
            Check(shortcuts.SetToggle(testKey), "Native toggle shortcut registers", results);
            Check(shortcuts.SetToggle(testKey), "Reapplying the same shortcut preserves its binding", results);
            Check(shortcuts.SetToggle(new Hotkey(3, 0x87)), "Native toggle shortcut can be replaced", results);
            Check(shortcuts.SetToggle(new Hotkey(3, 0x45)) && !shortcuts.EditReady, "Existing Ctrl+Alt+E toggle takes priority over the optional editor binding", results);
            Check(shortcuts.SetToggle(testKey), "Toggle changes restore the optional editor binding", results);
            results.Add(shortcuts.Ready ? "PASS Emergency shortcut registers" : "NOTICE Emergency shortcut already reserved; app correctly disables locking");
            results.Add(shortcuts.EditReady ? "PASS Cell editor shortcut registers" : "NOTICE Cell editor shortcut already reserved; button remains available");
        }
        using var iconStream = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/CursorJail.ico"))!.Stream;
        using var icon = new System.Drawing.Icon(iconStream);
        Check(icon.Width > 0, "Embedded tray icon loads", results);
        var native = new WindowsPlatform();
        var candidates = native.Windows();
        Check(candidates.All(w => w.Icon is not null), "Window dropdown provides app icons", results);
        var identified = candidates.Select(w => native.TryIdentifyWindow(w.Handle, out var target) ? target : null).FirstOrDefault(w => w is not null);
        if (identified is not null)
        {
            Check(identified.Identity.IsValid && native.IsWindowInstance(identified), "Read-only executable identity and process-instance validation", results);
            Check(native.TryClientRegion(identified, out _), "Foreign window client area resolves in physical pixels", results);
        }
        else results.Add("NOTICE No external identifiable window available for identity check");
        var dragTarget = candidates.Select(w => native.TryIdentifyWindow(w.Handle, out var target) ? target : null)
            .FirstOrDefault(w => w is not null && native.TryClientRegion(w, out var area) && area.Width >= 240 && area.Height >= 160
                && area.FitInside(WindowsPlatform.DesktopBounds(WindowsPlatform.Monitors())) == area);
        if (dragTarget is not null)
        {
            window.VerifyAttachedDraggingForSmoke(dragTarget);
            results.Add("PASS Attached resize smaller, move, and resize larger update the visible overlay immediately");
            results.Add("PASS Overlay Full, 16:9 and 21:9 presets keep the attachment and update its visible bounds");
        }
        else results.Add("NOTICE No suitable external window available for attached drag verification");
        using (var process = NativeMethods.OpenProcess(0x1000, false, (uint)Environment.ProcessId))
        {
            var executable = new System.Text.StringBuilder(32768);
            int capacity = executable.Capacity;
            Check(!process.IsInvalid && NativeMethods.QueryFullProcessImageName(process, 0, executable, ref capacity), "Limited-information process path query works", results);
        }
        using var selection = new PickerSmokeScope(WindowsPlatform.DesktopBounds(WindowsPlatform.Monitors()));
        Check(!NativeMethods.IsWindowVisible(selection.Handle), "Picker HWND can be created without taking focus", results);
        var point = new NativeMethods.Point { X = window.Region.Left + 10, Y = window.Region.Top + 10 };
        Check(NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(point), 2) != selection.Handle, "Hidden picker cannot select itself", results);
        overlay.SetMode(editing: true, visible: false);
        var desktop = WindowsPlatform.DesktopBounds(WindowsPlatform.Monitors());
        overlay.SetRegion(new(0, 0, 800, 450), desktop);
        Render(overlay, Path.Combine(outputDirectory, "CursorJail-editor.png"), 800, 450, 1);
        overlay.SetRegion(new(0, 0, 160, 100), desktop);
        Render(overlay, Path.Combine(outputDirectory, "CursorJail-editor-small.png"), 160, 100, 1);
        overlay.SetMode(editing: false, visible: false);
        Check(WindowsPlatform.IdentityWindowClass("HwndWrapper[Game;;guid1]") == WindowsPlatform.IdentityWindowClass("HwndWrapper[Game;;guid2]"),
            "WPF executable identities remain stable across window GUIDs", results);
        Check(WindowsPlatform.IdentityWindowClass("GameClass") == "GameClass", "Native game window classes are preserved", results);
        var picker = (ComboBox)window.FindName("WindowPicker");
        picker.ItemsSource = new[] { new WindowItem(0, "Choose a game or open window", WindowsPlatform.WindowIcon(0)) };
        picker.SelectedIndex = 0;
        ((Button)window.FindName("LockButton")).IsEnabled = true; // Presentation only; no input loop is run.
        // Render without presenting the panel or overlay to the input desktop.
        Render(window, Path.Combine(outputDirectory, "CursorJail-panel.png"), 620, 930, 1);
        Check(((ScrollViewer)window.FindName("PanelScroll")).ScrollableHeight < 1, "Collapsed manual panel fits without scrolling", results);
        Check(((Button)window.FindName("AttachButton")).Style == window.FindResource("Primary")
            && ((Button)window.FindName("LockButton")).Style != window.FindResource("Primary"), "Automatic setup has a single primary action", results);
        var manual = (Expander)window.FindName("ManualSetup");
        manual.IsExpanded = true;
        Check(((Button)window.FindName("LockButton")).Style == window.FindResource("Primary")
            && ((Button)window.FindName("AttachButton")).Style != window.FindResource("Primary"), "Manual expansion switches the primary action", results);
        manual.IsExpanded = false;
        Check(!((Button)window.FindName("HotkeyApply")).IsEnabled, "Unchanged shortcut Apply is disabled", results);
        Render(window, Path.Combine(outputDirectory, "CursorJail-panel-150.png"), 620, 930, 1.5);
        Render(window, Path.Combine(outputDirectory, "CursorJail-panel-compact.png"), 570, 600, 1);
        results.Add("Control panel rendered at 100%, 150%, and compact height");
        window.PreviewAttachmentForSmoke();
        Render(window, Path.Combine(outputDirectory, "CursorJail-attached.png"), 620, 930, 1);
        Check(((ScrollViewer)window.FindName("PanelScroll")).ScrollableHeight < 1, "Collapsed attached panel fits without scrolling", results);
        Check((string)((Button)window.FindName("AttachButton")).Content == "Detach from app", "Attached setup uses the combined detach action", results);
        Render(window, Path.Combine(outputDirectory, "CursorJail-attached-150.png"), 620, 930, 1.5);
        Render(window, Path.Combine(outputDirectory, "CursorJail-attached-compact.png"), 570, 600, 1);
        results.Add("Attached mode rendered at 100%, 150%, and compact height");
        File.WriteAllText(Path.Combine(outputDirectory, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        window.Close();
        Check(window.AutomaticPreferenceForSmoke, "Normal exit preserves the enabled automatic preference", results);
        File.WriteAllText(Path.Combine(outputDirectory, "results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
    }
    private static void Check(bool condition, string name, List<string> results)
    {
        if (!condition) throw new InvalidOperationException(name);
        results.Add("PASS " + name);
    }
    private static void Render(Window window, string path, double width, double height, double scale)
    {
        window.Width = width;
        window.Height = height;
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

    private sealed class PickerSmokeScope : IDisposable
    {
        private readonly TargetPickerWindow _window;
        internal nint Handle { get; }
        internal PickerSmokeScope(ScreenRect desktop)
        {
            _window = new TargetPickerWindow(desktop);
            Handle = new WindowInteropHelper(_window).EnsureHandle();
        }
        public void Dispose() => _window.Close();
    }
}
