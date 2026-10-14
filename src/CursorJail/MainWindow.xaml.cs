using System.Diagnostics;
using System.Globalization;
using System.Windows.Interop;
using CursorJail.Native;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace CursorJail;

public partial class MainWindow : Window
{
    private readonly bool _smoke;
    private readonly SettingsStore _store;
    private readonly AppSettings _settings;
    private readonly WindowsPlatform _platform = new();
    private readonly JailController _controller;
    private readonly AttachmentController _attachment;
    private readonly OverlayWindow _overlay = new();
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _countdown = new();
    private readonly NativeMethods.WinEventProc _foregroundCallback;
    private readonly NativeMethods.WinEventProc _moveCallback;
    private IReadOnlyList<MonitorItem> _monitors;
    private ScreenRect _desktop;
    private ScreenRect _region;
    private nint _snappedTarget;
    private uint _snappedProcess;
    private string _regionName = "Manual region";
    private nint _focusHook;
    private nint _moveHook;
    private GlobalHotkeys? _hotkeys;
    private Hotkey _draftHotkey;
    private Forms.NotifyIcon? _tray;
    private System.Drawing.Icon? _trayIcon;
    private bool _editing;
    private bool _initializing = true;
    private volatile bool _closing;
    private bool _pending;

    public MainWindow() : this(false) { }
    internal MainWindow(bool smoke)
    {
        _smoke = smoke;
        _store = new SettingsStore(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CursorJail", "settings.json"));
        _settings = smoke ? new() : _store.Load();
        _draftHotkey = _settings.ToggleHotkey;
        _platform.PassKey = _settings.PassKey;
        _platform.TrimWindow = _settings.TrimWindow;
        _controller = new JailController(_platform);
        _attachment = new AttachmentController(_platform, _controller);
        _monitors = WindowsPlatform.Monitors();
        if (_monitors.Count == 0) throw new InvalidOperationException("Windows did not report any connected monitors.");
        _desktop = WindowsPlatform.DesktopBounds(_monitors);
        _platform.RegionAvailable = RegionOnDesktop;
        if (_settings.Attachments.FirstOrDefault(p => p.Id == _settings.ActiveAttachmentId) is { } remembered)
            _attachment.Restore(remembered, _settings.AttachmentPaused);
        var primary = _monitors[0].Bounds;
        _region = NormalizeRegion(_settings.Region ?? ScreenRect.Center(primary, (int)(primary.Width * .7), (int)(primary.Height * .65)));
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += OnTick;
        _foregroundCallback = (_, eventType, window, _, _, _, _) =>
        {
            if (eventType == 3 && !_closing) _attachment.ForegroundChanged(window);
        };
        _moveCallback = (_, eventType, window, _, _, _, _) => { if (!_closing) _attachment.WindowMove(window, eventType == 10); };
        InitializeComponent();
        _overlay.SetRegion(_region, _desktop);
        _overlay.RegionChanged += region => SetRegion(region, persist: false);
        _overlay.EditFinished += () => SaveSettings();
        _overlay.PresetRequested += ApplyOverlayPreset;
        _overlay.EditorClosed += commit => { if (!_closing) EndCellEdit(commit, restoreFocus: _platform.ForegroundWindow == _overlay.Handle); };
        _controller.Changed += ControllerChanged;
        _attachment.Changed += AttachmentChanged;
        TrimCheck.IsChecked = _settings.TrimWindow;
        GlowCheck.IsChecked = _settings.EdgeGlow;
        PassPicker.SelectedIndex = (int)_settings.PassKey;
        HotkeyInput.Text = GlobalHotkeys.Label(_settings.ToggleHotkey);
        MonitorPicker.ItemsSource = _monitors;
        MonitorPicker.SelectedIndex = 0;
        RefreshWindowList();
        RefreshSaved();
        RefreshRememberedApps();
        UpdateDimensions();
        _initializing = false;
        RefreshUi();
        if (_attachment.Profile is not null) ShowStatus(_attachment.Message);
        if (_store.Notice is not null) ShowStatus(_store.Notice);
        SourceInitialized += InitializeNative;
        Closed += OnClosed;
        Loaded += (_, _) =>
        {
            MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 24);
            Height = Math.Min(Height, MaxHeight);
        };
    }

    private void InitializeNative(object? sender, EventArgs e)
    {
        if (_smoke) return;
        _hotkeys = new GlobalHotkeys(new WindowInteropHelper(this).Handle);
        _hotkeys.Toggle += ToggleLock;
        _hotkeys.Emergency += () => Release("Emergency release. Your cursor is free.");
        _hotkeys.Edit += () =>
        {
            if (HotkeyInput.IsKeyboardFocusWithin)
            {
                _draftHotkey = new Hotkey(3, 0x45);
                HotkeyInput.Text = GlobalHotkeys.Label(_draftHotkey);
                RefreshVisualState();
                ShowStatus("Press Apply to use Ctrl + Alt + E to lock and unlock. Adjust cursor area will remain available by button.");
            }
            else if (_editing) EndCellEdit(true, true);
            else BeginCellEdit();
        };
        _hotkeys.SetToggle(_settings.ToggleHotkey);
        _focusHook = NativeMethods.SetWinEventHook(3, 3, 0, _foregroundCallback, 0, 0, 0);
        _moveHook = NativeMethods.SetWinEventHook(10, 11, 0, _moveCallback, 0, 0, 0);
        _attachment.SetReady(_hotkeys.Ready && _focusHook != 0);
        if (_focusHook == 0) ShowStatus("Windows focus monitoring is unavailable. Locking is disabled.");
        else if (_hotkeys.Error is not null) ShowStatus(_hotkeys.Error);
        SystemEvents.DisplaySettingsChanged += DisplayChanged;
        SystemEvents.SessionSwitch += SessionChanged;
        SystemEvents.PowerModeChanged += PowerChanged;
        CreateTray();
        RefreshUi();
        RefreshTimer();
    }

    private bool CanLock()
    {
        if (_closing) return false;
        if (_hotkeys is not { Ready: true }) { ShowStatus(_hotkeys?.Error ?? "Release shortcuts are unavailable. Locking is disabled."); return false; }
        if (_focusHook == 0) { ShowStatus("Focus monitoring is unavailable. Locking is disabled."); return false; }
        return true;
    }

    private void ToggleLock()
    {
        if (_attachment.Profile is not null) { ToggleAutomatic(); return; }
        if (_pending || _controller.State != JailState.Unlocked) { Release(); return; }
        if (!CanLock()) return;
        LockForeground();
    }

    private bool SnapAvailable()
    {
        NativeMethods.GetWindowThreadProcessId(_snappedTarget, out uint process);
        return _snappedTarget != 0 && process == _snappedProcess && _platform.IsUsableWindow(_snappedTarget);
    }

    private void LockForeground()
    {
        _pending = false;
        _countdown.Reset();
        nint target = _platform.ForegroundWindow;
        if (_snappedTarget != 0 && (!SnapAvailable() || target != _snappedTarget))
        {
            ShowStatus("Focus the selected window, then use your lock shortcut. Or choose a monitor layout for a manual cursor area.");
            _timer.Stop();
            RefreshUi();
            return;
        }
        if (_snappedTarget != 0)
        {
            if (!_platform.TryGetTargetRegion(target, out var region) || !RegionOnDesktop(region))
            {
                ShowStatus("The selected window is outside the desktop or too small. Use selected window to set the area again.");
                _timer.Stop();
                RefreshUi();
                return;
            }
            _region = region;
        }
        _editing = false;
        _overlay.SetMode(false, false);
        _controller.Lock(_region, target, _snappedTarget != 0);
        RefreshUi();
    }

    private void LockFromPanel(object sender, RoutedEventArgs e)
    {
        if (_attachment.Profile is not null) { ToggleAutomatic(returnToGame: true); return; }
        if (_pending || _controller.State != JailState.Unlocked) { Release(); return; }
        if (!CanLock()) return;
        if (_snappedTarget != 0 && !SnapAvailable()) { ShowStatus("The selected window is no longer available. Refresh the list and choose another window."); return; }
        _editing = false;
        _overlay.SetMode(false, false);
        _pending = true;
        _countdown.Restart();
        _timer.Start();
        ShowStatus("Locking in 3 seconds. Focus your app; either lock or release shortcut cancels.");
        RefreshUi();
        WindowState = WindowState.Minimized;
        if (_snappedTarget != 0) NativeMethods.SetForegroundWindow(_snappedTarget);
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (_closing) { _timer.Stop(); return; }
        if (_pending)
        {
            if (_platform.IsEscapeChordDown) { Release("Lock cancelled by a system shortcut."); return; }
            int remaining = Math.Max(0, 3 - (int)_countdown.Elapsed.TotalSeconds);
            LockButton.Content = $"Cancel lock ({remaining}s)";
            if (_countdown.Elapsed >= TimeSpan.FromSeconds(3))
            {
                LockForeground();
                if (!_controller.IsArmed)
                {
                    _tray?.ShowBalloonTip(3500, "CursorJail · cursor remains unlocked", StatusMessage.Text, Forms.ToolTipIcon.Info);
                }
            }
            return;
        }
        if (_attachment.Profile is not null)
        {
            if (_editing) UpdateAttachedPreview();
            _attachment.Tick();
            if (!_attachment.Enabled) _controller.Tick();
        }
        else _controller.Tick();
        if (_attachment.Profile is null && _controller.State == JailState.Locked && _settings.EdgeGlow) _overlay.UpdateGlow();
    }

    private void ControllerChanged()
    {
        if (_closing) return;
        if (_controller.IsArmed)
        {
            if (!RegionOnDesktop(_controller.Region)) { Release("Released. The cursor area moved outside the desktop."); return; }
            _region = _controller.Region;
            _overlay.SetRegion(_region, _desktop);
            UpdateDimensions();
        }
        RefreshTimer();
        _overlay.SetMode(_editing, _editing || (_attachment.Profile is null && _controller.State == JailState.Locked && _settings.EdgeGlow));
        ShowStatus(_controller.Message);
        RefreshUi();
    }

    private void Release(string message = "Released. Your cursor is free.")
    {
        _pending = false;
        _countdown.Reset();
        _picker?.Cancel();
        if (_editing) EndCellEdit(commit: false, restoreFocus: false);
        if (_attachment.Profile is not null) _attachment.Pause(message + " Automatic locking is paused.");
        else _controller.Release(message);
        SaveSettings();
        RefreshTimer();
        RefreshUi();
    }

    private void SetRegion(ScreenRect region, bool clearTarget = true, bool persist = true)
    {
        if (_attachment.Profile is not null)
        {
            if (!_attachment.SetCustomCell(region)) { ShowStatus("Keep the cursor area inside the attached app window."); return; }
            // Let the preview compare against the previous rectangle before updating the overlay.
            UpdateAttachedPreview();
            UpdateDimensions();
            if (persist) SaveSettings();
            return;
        }
        if (_controller.State != JailState.Unlocked || _pending) Release();
        _region = NormalizeRegion(region);
        if (clearTarget) { _snappedTarget = 0; _snappedProcess = 0; _regionName = "Manual region"; }
        _overlay.SetRegion(_region, _desktop);
        UpdateDimensions();
        if (persist) SaveSettings();
        RefreshUi();
    }

    private bool RegionOnDesktop(ScreenRect region) => region.IsValid && region.Left >= _desktop.Left && region.Top >= _desktop.Top
        && region.Right <= _desktop.Right && region.Bottom <= _desktop.Bottom && _monitors.Any(m =>
            Math.Min(region.Right, m.Bounds.Right) - Math.Max(region.Left, m.Bounds.Left) >= 160
            && Math.Min(region.Bottom, m.Bounds.Bottom) - Math.Max(region.Top, m.Bounds.Top) >= 100);

    private ScreenRect NormalizeRegion(ScreenRect region)
    {
        if (!region.IsValid) region = ScreenRect.Center(_monitors[0].Bounds, 800, 450);
        var fitted = region.FitInside(_desktop);
        return RegionOnDesktop(fitted) ? fitted : fitted.FitInside(_monitors[0].Bounds);
    }

    private void UpdateDimensions()
    {
        XInput.Text = _region.Left.ToString(CultureInfo.InvariantCulture);
        YInput.Text = _region.Top.ToString(CultureInfo.InvariantCulture);
        WidthInput.Text = _region.Width.ToString(CultureInfo.InvariantCulture);
        HeightInput.Text = _region.Height.ToString(CultureInfo.InvariantCulture);
        RegionSummary.Text = $"{_region.Width:N0} × {_region.Height:N0} px";
        WindowPicker.ToolTip = _regionName;
    }

    private void RefreshUi()
    {
        bool busy = _pending || _controller.State != JailState.Unlocked;
        SetupControls.IsEnabled = !busy;
        EditButton.IsEnabled = !busy;
        EditButton.Content = _editing ? "Done editing" : "Adjust cursor area";
        LockButton.IsEnabled = busy || (_hotkeys is { Ready: true } && _focusHook != 0);
        LockButton.Content = _pending ? "Cancel lock" : _controller.State != JailState.Unlocked ? "Release cursor" : "Lock cursor in 3s";
        StateLabel.Text = _pending ? "ARMING" : _controller.State switch
        {
            JailState.Locked => "LOCKED", JailState.Passing => "TEMPORARILY UNLOCKED",
            JailState.ReleaseFailed => "RETRYING RELEASE", _ => _editing ? "EDITING" : "READY"
        };
        EscapeHint.Text = $"{GlobalHotkeys.Label(_settings.ToggleHotkey)}  lock / unlock     ·     Ctrl + Alt + Esc  release";
        RefreshAttachmentUi();
        RefreshVisualState();
        if (_tray is not null) _tray.Text = "CursorJail · " + StateLabel.Text.ToLowerInvariant();
    }

    private void ShowStatus(string message) => StatusMessage.Text = _store.Notice is { } notice && message != notice
        ? message + "\n" + notice : message;
    private bool SaveSettings()
    {
        if (_smoke || _closing) return true;
        _settings.Region = _region;
        SyncAttachmentSettings();
        if (_store.Save(_settings)) return true;
        ShowStatus(_store.Notice!);
        return false;
    }
    private void RefreshWindowList()
    {
        nint selected = (WindowPicker.SelectedItem as WindowItem)?.Handle ?? 0;
        var windows = _platform.Windows();
        WindowPicker.ItemsSource = windows;
        WindowPicker.SelectedItem = windows.FirstOrDefault(w => w.Handle == selected) ?? windows.FirstOrDefault();
    }
    private void RefreshWindows(object sender, RoutedEventArgs e) => RefreshWindowList();
    private void SnapWindow(object sender, RoutedEventArgs e)
    {
        if (WindowPicker.SelectedItem is not WindowItem window || !_platform.IsUsableWindow(window.Handle)
            || !WindowsPlatform.TryWindowRegion(window.Handle, _settings.TrimWindow, out var rect) || !RegionOnDesktop(rect))
        { ShowStatus("Choose a visible window at least 160 × 100 pixels, then click Use selected window."); return; }
        _attachment.Detach();
        _snappedTarget = window.Handle;
        NativeMethods.GetWindowThreadProcessId(window.Handle, out _snappedProcess);
        _regionName = window.Title;
        SetRegion(rect, clearTarget: false);
        BeginCellEdit();
        ShowStatus($"Area set from {window.Title}. Dragging the area stops it following the window.");
        RefreshUi();
    }
    private void TrimChanged(object sender, RoutedEventArgs e)
    {
        _settings.TrimWindow = TrimCheck.IsChecked == true;
        _platform.TrimWindow = _settings.TrimWindow;
        if (_snappedTarget != 0 && SnapAvailable() && _platform.TryGetTargetRegion(_snappedTarget, out var region))
            SetRegion(region, clearTarget: false);
        SaveSettings();
    }
    private void UseLayout(object sender, RoutedEventArgs e)
    {
        if (MonitorPicker.SelectedItem is not MonitorItem monitor) return;
        if (!Enum.TryParse<RegionPreset>((string)((Button)sender).Tag, out var preset)) return;
        _attachment.Detach();
        SetRegion(ScreenRect.Preset(monitor.Bounds, preset));
        BeginCellEdit();
        ShowStatus("Layout ready. Adjust the cursor area, then lock it.");
        RefreshUi();
    }
    private void ToggleEdit(object sender, RoutedEventArgs e)
    {
        if (_editing) EndCellEdit(true, true);
        else BeginCellEdit();
    }
    private void ApplyDimensions(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(XInput.Text, out int x) || !int.TryParse(YInput.Text, out int y)
            || !int.TryParse(WidthInput.Text, out int width) || !int.TryParse(HeightInput.Text, out int height)
            || width < 160 || height < 100 || width > _desktop.Width || height > _desktop.Height
            || x < _desktop.Left || y < _desktop.Top || (long)x + width > _desktop.Right || (long)y + height > _desktop.Bottom)
        { ShowStatus("Enter whole pixel values inside the desktop. Minimum area: 160 × 100 pixels."); return; }
        var region = new ScreenRect(x, y, x + width, y + height);
        if (!RegionOnDesktop(region)) { ShowStatus("Place the cursor area on a connected monitor."); return; }
        SetRegion(region);
        ShowStatus("Exact dimensions applied.");
    }
    private void CaptureHotkey(object sender, KeyEventArgs e)
    {
        Key key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Tab) return;
        e.Handled = true;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return;
        uint modifiers = 0;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) modifiers |= 1;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) modifiers |= 2;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)) modifiers |= 4;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) modifiers |= 8;
        var candidate = new Hotkey(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key));
        if (!candidate.IsValid || candidate == Hotkey.Emergency || (modifiers & 8) != 0)
        { ShowStatus("Use Ctrl, Alt, Shift, Scroll Lock or a function key. Ctrl + Alt + Esc is reserved for release."); return; }
        _draftHotkey = candidate;
        HotkeyInput.Text = GlobalHotkeys.Label(candidate);
        RefreshVisualState();
        ShowStatus(candidate == _settings.ToggleHotkey ? "This shortcut is already active." : "Press Apply to use this shortcut.");
    }
    private void ApplyHotkey(object sender, RoutedEventArgs e)
    {
        if (_draftHotkey == _settings.ToggleHotkey && _hotkeys is { Ready: true })
        { ShowStatus("This shortcut is already active."); return; }
        if (_hotkeys is null || !_hotkeys.SetToggle(_draftHotkey))
        { ShowStatus(_hotkeys?.Error ?? "Shortcut registration is unavailable."); return; }
        _settings.ToggleHotkey = _draftHotkey;
        _attachment.SetReady(_hotkeys.Ready && _focusHook != 0);
        SaveSettings();
        ShowStatus(_hotkeys.Error ?? "Lock / unlock shortcut saved.");
        BuildTrayMenu();
        RefreshUi();
    }
    private void PassChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_initializing || PassPicker.SelectedIndex < 0) return;
        _settings.PassKey = (PassKey)PassPicker.SelectedIndex;
        _platform.PassKey = _settings.PassKey;
        SaveSettings();
    }
    private void GlowChanged(object sender, RoutedEventArgs e)
    {
        _settings.EdgeGlow = GlowCheck.IsChecked == true;
        SaveSettings();
    }
    private void RefreshSaved()
    {
        SavedPicker.ItemsSource = _settings.Presets.ToList();
        SavedPicker.SelectedIndex = _settings.Presets.Count > 0 ? 0 : -1;
        if (_tray is not null) BuildTrayMenu();
    }
    private void SavePreset(object sender, RoutedEventArgs e)
    {
        string name = PresetName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name)) { ShowStatus("Give this cursor area a name before saving."); return; }
        int existing = _settings.Presets.FindIndex(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        var preset = new SavedRegion(name, _region);
        if (existing >= 0) _settings.Presets[existing] = preset;
        else if (_settings.Presets.Count < 20) _settings.Presets.Add(preset);
        else { ShowStatus("You can save 20 cursor areas. Delete one or reuse its name."); return; }
        RefreshSaved();
        if (SaveSettings()) ShowStatus($"Saved “{name}”. Loading it restores a manual cursor area.");
    }
    private void LoadPreset(object sender, RoutedEventArgs e)
    {
        if (SavedPicker.SelectedItem is SavedRegion preset) ApplySaved(preset);
        else ShowStatus("Choose a saved cursor area first.");
    }
    private void ApplySaved(SavedRegion preset)
    {
        Release();
        _attachment.Detach();
        SetRegion(preset.Region);
        BeginCellEdit();
        ShowStatus($"Loaded “{preset.Name}”. Check the cursor area before locking.");
        RefreshUi();
    }
    private void DeletePreset(object sender, RoutedEventArgs e)
    {
        if (SavedPicker.SelectedItem is not SavedRegion preset) { ShowStatus("Choose a saved cursor area first."); return; }
        _settings.Presets.Remove(preset);
        RefreshSaved();
        SaveSettings();
        ShowStatus($"Deleted “{preset.Name}”.");
    }

    private void CreateTray()
    {
        using var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/CursorJail.ico"))!.Stream;
        using var icon = new System.Drawing.Icon(resource);
        _trayIcon = (System.Drawing.Icon)icon.Clone();
        _tray = new Forms.NotifyIcon { Icon = _trayIcon, Text = "CursorJail · ready", Visible = true };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(ShowPanel);
        BuildTrayMenu();
    }
    private void BuildTrayMenu()
    {
        if (_tray is null) return;
        var old = _tray.ContextMenuStrip;
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open CursorJail", null, (_, _) => Dispatcher.Invoke(ShowPanel));
        menu.Items.Add(_attachment.Profile is null ? "Lock cursor in 3s" : _attachment.Enabled ? "Pause automatic locking" : "Resume automatic locking", null,
            (_, _) => Dispatcher.Invoke(() => LockFromPanel(this, new RoutedEventArgs())));
        menu.Items.Add(_hotkeys?.EditReady == true ? "Adjust cursor area · Ctrl + Alt + E" : "Adjust cursor area", null, (_, _) => Dispatcher.Invoke(() => BeginCellEdit()));
        menu.Items.Add("Release cursor", null, (_, _) => Dispatcher.Invoke(() => Release()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        var saved = new Forms.ToolStripMenuItem("Saved cursor areas") { Enabled = _settings.Presets.Count > 0 };
        foreach (var preset in _settings.Presets)
            saved.DropDownItems.Add(preset.Name, null, (_, _) => Dispatcher.Invoke(() => { ShowPanel(); ApplySaved(preset); }));
        menu.Items.Add(saved);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit CursorJail", null, (_, _) => Dispatcher.Invoke(Close));
        _tray.ContextMenuStrip = menu;
        old?.Dispose();
    }
    private void ShowPanel()
    {
        if (_editing) EndCellEdit(true, false);
        _attachment.SetEditing(true);
        _controller.Release();
        Show();
        WindowState = WindowState.Normal;
        Activate();
        _attachment.SetEditing(false);
        RefreshWindowList();
    }
    private void HideToTray(object sender, RoutedEventArgs e) => Hide();
    private void ExitApp(object sender, RoutedEventArgs e) => Close();
    private void DragTitle(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.OriginalSource is not Button) DragMove();
    }
    private void PanelKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape || HotkeyInput.IsKeyboardFocusWithin) return;
        Release();
        _editing = false;
        _overlay.SetMode(false, false);
        RefreshUi();
        e.Handled = true;
    }
    private void DisplayChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (_closing) return;
        Release("Released. The display layout changed.");
        var monitors = WindowsPlatform.Monitors();
        if (monitors.Count == 0) return;
        _monitors = monitors;
        _desktop = WindowsPlatform.DesktopBounds(monitors);
        MonitorPicker.ItemsSource = monitors;
        MonitorPicker.SelectedIndex = 0;
        if (_attachment.Profile is not null)
        {
            UpdateAttachedPreview(); // Recompute geometry without changing the saved layout/margins.
            SaveSettings();
            RefreshUi();
        }
        else SetRegion(_region);
    }));
    private void SessionChanged(object sender, SessionSwitchEventArgs e) => Dispatcher.BeginInvoke(new Action(() =>
    {
        if (!_closing) Release("Released. The Windows session changed.");
    }));
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend) Dispatcher.Invoke(() => Release("Released. The computer is entering sleep."));
        else Dispatcher.BeginInvoke(new Action(() => { if (!_closing) Release("Released. Power state changed."); }));
    }

    internal bool ReleaseForExit()
    {
        // Also safe on ProcessExit, where the dispatcher may already have stopped.
        _closing = true;
        _pending = false;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            _controller.Release("Released. CursorJail is closing.");
            if (_controller.State != JailState.ReleaseFailed) break;
        }
        return _controller.State == JailState.Unlocked;
    }
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_closing)
        {
            if (_editing) EndCellEdit(true, false);
            _pending = false;
            _attachment.SetEditing(true); // Suspend during close without changing the saved automatic preference.
            _controller.Release("Released. CursorJail is closing.");
            if (_controller.State == JailState.ReleaseFailed)
            {
                e.Cancel = true;
                _attachment.Pause("Close cancelled. Windows could not confirm cursor release; automatic locking is paused.");
                _attachment.SetEditing(false);
            }
        }
        base.OnClosing(e);
    }
    private void OnClosed(object? sender, EventArgs e)
    {
        SaveSettings();
        _closing = true;
        ReleaseForExit();
        _timer.Stop();
        if (_focusHook != 0) NativeMethods.UnhookWinEvent(_focusHook);
        if (_moveHook != 0) NativeMethods.UnhookWinEvent(_moveHook);
        _picker?.Cancel();
        _hotkeys?.Dispose();
        if (!_smoke)
        {
            SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            SystemEvents.SessionSwitch -= SessionChanged;
            SystemEvents.PowerModeChanged -= PowerChanged;
        }
        _tray?.ContextMenuStrip?.Dispose();
        _tray?.Dispose();
        _trayIcon?.Dispose();
        _overlay.Close();
        if (!_smoke) Application.Current.Shutdown();
    }
    internal OverlayWindow Overlay => _overlay;
    internal ScreenRect Region => _region;
}
