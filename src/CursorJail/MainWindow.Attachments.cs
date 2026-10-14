using System.Text;
using CursorJail.Native;

namespace CursorJail;

public partial class MainWindow
{
    private TargetPickerWindow? _picker;
    private AttachmentProfile? _editProfileSnapshot;
    private ScreenRect _editRegionSnapshot;
    private ScreenRect _editorBounds;
    private nint _returnFocusTo;
    private bool _endingEditor;

    private void RefreshTimer()
    {
        if (!_closing && (_pending || _controller.State != JailState.Unlocked
            || (_attachment.Profile is not null && (_attachment.Enabled || _editing)))) _timer.Start();
        else _timer.Stop();
    }

    private void SyncAttachmentSettings()
    {
        var profile = _attachment.Profile;
        _settings.ActiveAttachmentId = profile?.Id;
        _settings.AttachmentPaused = profile is not null && !_attachment.Enabled;
        if (profile is null) return;
        if (_attachment.Window is null) RegionSummary.Text = "waiting for window";
        int index = _settings.Attachments.FindIndex(p => p.Id == profile.Id);
        if (index >= 0) _settings.Attachments[index] = profile;
        else if (_settings.Attachments.Count < 20) _settings.Attachments.Add(profile);
    }

    private void AttachmentChanged()
    {
        if (_initializing || _closing) return;
        bool persistPause = _settings.ActiveAttachmentId is not null && (_attachment.Profile is null
            || _settings.AttachmentPaused != !_attachment.Enabled);
        SyncAttachmentSettings();
        RefreshTimer();
        RefreshUi();
        if (_attachment.Profile is not null) ShowStatus(_attachment.Message);
        if (persistPause) SaveSettings();
    }

    private void RefreshAttachmentUi()
    {
        var profile = _attachment.Profile;
        RememberedApps.Visibility = _settings.Attachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        AttachmentDetails.Text = profile is null ? "Attach once. Lock automatically only while that game has focus."
            : $"{profile.Name} · {(_attachment.Enabled ? "Automatic locking enabled" : "Automatic locking paused")}";
        CellControls.Visibility = profile is null ? Visibility.Collapsed : Visibility.Visible;
        DetachButton.IsEnabled = profile is not null;
        GlowCheck.IsEnabled = profile is null;
        EditHint.Text = _hotkeys?.EditReady == true || _smoke ? "Attached cells stay invisible while playing. Ctrl + Alt + E adjusts the cell."
            : "Attached cells stay invisible while playing. Use Adjust cell to edit.";
        TrimCheck.IsEnabled = profile is null;
        ModeHint.Text = profile is null ? "Manual cages release on focus loss. Toggle again to lock."
            : "Focus the attached app to lock. Switch away to release. Emergency release pauses automatic locking.";
        if (profile is null) return;
        CellDescription.Text = profile.Layout switch
        {
            CellLayout.Center16By9 => "Centered 16:9 cell · follows the game canvas",
            CellLayout.Center21By9 => "Centered 21:9 cell · follows the game canvas",
            CellLayout.Custom => "Custom cell · margins scale with the game canvas",
            _ => "Match game canvas · title bar and native borders excluded"
        };
        StateLabel.Text = _controller.State == JailState.ReleaseFailed ? "RETRYING RELEASE" : _editing ? "EDITING CELL"
            : !_attachment.Enabled ? "AUTO PAUSED" : _controller.State == JailState.Locked ? "AUTO LOCKED"
            : _controller.State == JailState.Passing ? "ON PAROLE" : "ATTACHED · WAITING";
        LockButton.Content = _controller.State == JailState.ReleaseFailed ? "Retry release" : _attachment.Enabled ? "Pause automatic locking" : "Resume automatic locking";
        LockButton.IsEnabled = _controller.State == JailState.ReleaseFailed || _attachment.Enabled || (_hotkeys is { Ready: true } && _focusHook != 0);
        EditButton.Content = _editing ? "Done editing" : "Adjust cell";
        EditButton.IsEnabled = _controller.State != JailState.ReleaseFailed && _attachment.Window is not null;
        if (_tray is not null) _tray.Text = "CursorJail · " + (_attachment.Enabled ? "automatic" : "paused");
    }

    private void AttachWindow(object sender, RoutedEventArgs e)
    {
        if (WindowPicker.SelectedItem is WindowItem window) AttachHandle(window.Handle);
        else ShowStatus("Choose an open game window first.");
    }

    private void AttachHandle(nint handle)
    {
        if (!_platform.TryIdentifyWindow(handle, out var window) || !_platform.TryClientRegion(window, out _))
        {
            _attachment.SetEditing(false);
            ShowPanel();
            ShowStatus("Could not identify that window. Choose a visible game or app running at normal privileges.");
            return;
        }
        var title = new StringBuilder(512);
        NativeMethods.GetWindowText(handle, title, title.Capacity);
        string name = title.Length > 0 ? title.ToString() : Path.GetFileName(window.Identity.ExecutablePath);
        if (name.Length > 128) name = name[..128];
        var profile = _settings.Attachments.FirstOrDefault(p => p.Identity.Matches(window.Identity))
            ?? new AttachmentProfile(Guid.NewGuid().ToString("N"), name, window.Identity, CellLayout.MatchClient, default);
        if (!_settings.Attachments.Any(p => p.Id == profile.Id) && _settings.Attachments.Count >= 20)
        { _attachment.SetEditing(false); ShowPanel(); ShowStatus("20 apps are remembered. Forget one before attaching another."); return; }
        if (!_attachment.Attach(profile, window)) { ShowStatus("The window changed or cursor release failed. Try attaching again."); return; }
        _snappedTarget = 0;
        _snappedProcess = 0;
        _regionName = profile.Name;
        RefreshRememberedApps();
        SaveSettings();
        BuildTrayMenu();
        BeginCellEdit();
    }

    private void PickWindow(object sender, RoutedEventArgs e)
    {
        if (_picker is not null || _closing) return;
        if (_editing) EndCellEdit(true, false);
        _attachment.SetEditing(true);
        _controller.Release("Selecting a window. Cursor is free.");
        if (_controller.State == JailState.ReleaseFailed) return;
        _overlay.SetMode(false, false);
        Hide();
        _picker = new TargetPickerWindow(_desktop);
        _picker.Selected += handle =>
        {
            _picker = null;
            if (_closing) return;
            if (handle != 0) AttachHandle(handle);
            else { _attachment.SetEditing(false); ShowPanel(); ShowStatus("Window selection cancelled."); }
        };
        _picker.Show();
    }

    private void DetachWindow(object sender, RoutedEventArgs e)
    {
        if (_editing) EndCellEdit(true, false);
        _attachment.Detach();
        _snappedTarget = 0;
        _regionName = "Manual region";
        SaveSettings();
        BuildTrayMenu();
        RefreshUi();
        ShowStatus("Detached. The current rectangle is now a manual cage.");
    }

    private void ToggleAutomatic(bool returnToGame = false)
    {
        if (_closing || _attachment.Profile is null) return;
        if (_controller.State == JailState.ReleaseFailed) { _attachment.Pause("Retrying cursor release. Automatic locking stays paused."); return; }
        if (_attachment.Enabled) _attachment.Pause();
        else
        {
            if (!CanLock()) return;
            _attachment.Resume();
            if (returnToGame)
            {
                Hide();
                if (_attachment.Window is { } window && _platform.IsWindowInstance(window)) NativeMethods.SetForegroundWindow(window.Handle);
            }
            _attachment.Tick();
        }
        SaveSettings();
        BuildTrayMenu();
        RefreshTimer();
    }

    private void ChooseCell(object sender, RoutedEventArgs e)
    {
        if (!Enum.TryParse<CellLayout>((string)((Button)sender).Tag, out var layout) || _attachment.Profile is not { } original) return;
        _attachment.SetLayout(layout);
        if (_attachment.Window is not null && _platform.TryClientRegion(_attachment.Window, out _)) BeginCellEdit(original);
        else { SaveSettings(); ShowStatus("Cell layout saved. Open the remembered app to use it."); }
    }

    private void BeginCellEdit(AttachmentProfile? profileSnapshot = null)
    {
        if (_closing || _picker is not null) return;
        if (_editing) { UpdateAttachedPreview(); return; }
        var foreground = _platform.ForegroundWindow;
        _returnFocusTo = _attachment.Window?.Handle ?? (_snappedTarget != 0 ? _snappedTarget : _platform.IsUsableWindow(foreground) ? foreground : 0);
        if (_attachment.Profile is not null && (_attachment.Window is null || !_platform.TryClientRegion(_attachment.Window, out _)))
        { ShowStatus("Open the attached app and focus it once before adjusting the cell."); return; }
        _pending = false;
        _countdown.Reset();
        _editRegionSnapshot = _region;
        _editProfileSnapshot = profileSnapshot ?? _attachment.Profile;
        _attachment.SetEditing(true);
        _controller.Release("Editing the cage. Your cursor is free.");
        if (_controller.State == JailState.ReleaseFailed) { _attachment.SetEditing(false); return; }
        _editing = true;
        _editorBounds = default;
        if (_attachment.Profile is not null) UpdateAttachedPreview();
        else _overlay.SetRegion(_region, _desktop);
        if (!_editing) return;
        Hide();
        _overlay.SetMode(true, true);
        NativeMethods.SetForegroundWindow(_overlay.Handle);
        ShowStatus("Adjust the cell. Done returns to the game; Esc cancels and pauses automatic locking.");
        RefreshUi();
        RefreshTimer();
    }

    private void UpdateAttachedPreview()
    {
        if (_attachment.Profile is not { } profile || _attachment.Window is not { } window) return;
        if (!_platform.TryClientRegion(window, out var client))
        { if (_editing) EndCellEdit(false, false); return; }
        var bounds = new ScreenRect(Math.Max(client.Left, _desktop.Left), Math.Max(client.Top, _desktop.Top),
            Math.Min(client.Right, _desktop.Right), Math.Min(client.Bottom, _desktop.Bottom));
        if (!bounds.IsValid) { if (_editing) EndCellEdit(false, false); return; }
        var cell = profile.Cell(client);
        if (!cell.IsValid)
        { if (_editing) EndCellEdit(false, false); ShowStatus("The remembered cell is too small at this window size. Choose Match canvas or enlarge the window."); return; }
        cell = cell.FitInside(bounds);
        if (_region != cell || _editorBounds != bounds)
        {
            _region = cell;
            _editorBounds = bounds;
            _overlay.SetRegion(cell, bounds);
            UpdateDimensions();
        }
    }

    private void EndCellEdit(bool commit, bool restoreFocus)
    {
        if (!_editing || _endingEditor) return;
        _endingEditor = true;
        try
        {
            _editing = false;
            if (!commit)
            {
                if (_editProfileSnapshot is not null) _attachment.RestoreCell(_editProfileSnapshot);
                _region = NormalizeRegion(_editRegionSnapshot);
                if (_attachment.Profile is not null) _attachment.Pause("Editor cancelled. Automatic locking is paused.");
            }
            _attachment.SetEditing(false);
            _overlay.SetMode(false, false);
            _overlay.SetRegion(_region, _desktop);
            UpdateDimensions();
            SaveSettings();
            RefreshRememberedApps();
            RefreshTimer();
            RefreshUi();
            if (restoreFocus && commit && _returnFocusTo != 0 && _platform.IsUsableWindow(_returnFocusTo))
            {
                if (_attachment.Window is not { } target || _platform.IsWindowInstance(target)) NativeMethods.SetForegroundWindow(_returnFocusTo);
            }
            else if (restoreFocus) ShowPanel();
        }
        finally { _endingEditor = false; }
    }

    private void RefreshRememberedApps()
    {
        if (_attachment.Profile is { } profile) SyncAttachmentSettings();
        RememberedPicker.ItemsSource = _settings.Attachments.ToList();
        RememberedPicker.SelectedItem = _settings.Attachments.FirstOrDefault(p => p.Id == _settings.ActiveAttachmentId)
            ?? _settings.Attachments.FirstOrDefault();
    }

    private void LoadRemembered(object sender, RoutedEventArgs e)
    {
        if (RememberedPicker.SelectedItem is not AttachmentProfile profile) { ShowStatus("Choose a remembered app first."); return; }
        _controller.Release();
        if (_controller.State == JailState.ReleaseFailed) return;
        _attachment.Restore(profile, paused: false);
        SaveSettings();
        RefreshTimer();
        BuildTrayMenu();
        ShowStatus("Remembered app selected. Focus it to attach automatically.");
    }

    private void ForgetRemembered(object sender, RoutedEventArgs e)
    {
        if (RememberedPicker.SelectedItem is not AttachmentProfile profile) { ShowStatus("Choose a remembered app first."); return; }
        if (_attachment.Profile?.Id == profile.Id) _attachment.Detach();
        _settings.Attachments.RemoveAll(p => p.Id == profile.Id);
        RefreshRememberedApps();
        SaveSettings();
        BuildTrayMenu();
        ShowStatus($"Forgot “{profile.Name}”.");
    }

    internal void PreviewAttachmentForSmoke()
    {
        if (!_smoke) throw new InvalidOperationException("Preview requires smoke mode.");
        _attachment.Restore(new AttachmentProfile(Guid.NewGuid().ToString("N"), "Game window",
            new AppIdentity(@"C:\Games\Game.exe", "GameClass"), CellLayout.Center16By9, default), paused: false);
        ((ComboBox)FindName("WindowPicker")).ItemsSource = new[] { new WindowItem(0, "Game window", WindowsPlatform.WindowIcon(0)) };
        WindowPicker.SelectedIndex = 0;
        RefreshRememberedApps();
        RefreshUi();
        EditButton.IsEnabled = true; // Rendering only; attachment services remain unavailable.
    }
    internal bool AutomaticPreferenceForSmoke => _attachment.Enabled;
}
