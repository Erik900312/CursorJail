namespace CursorJail;

public partial class MainWindow
{
    private void ManualContextChanged(object sender, RoutedEventArgs e)
    {
        if (!_initializing) RefreshVisualState();
    }

    private void RefreshVisualState()
    {
        bool attached = _attachment.Profile is not null;
        bool busy = _pending || _controller.State != JailState.Unlocked;
        bool manual = !attached && ManualSetup.IsExpanded;
        AttachButton.Style = (Style)FindResource(!attached && !manual && !busy ? "Primary" : typeof(Button));
        LockButton.Style = (Style)FindResource(attached || manual || busy ? "Primary" : typeof(Button));
        HotkeyApply.IsEnabled = _draftHotkey != _settings.ToggleHotkey && (_smoke || _hotkeys is not null);

        bool warning = _pending || _controller.State is JailState.ReleaseFailed or JailState.Passing
            || (attached && !_attachment.Enabled);
        bool active = !warning && !_editing && (_controller.State == JailState.Locked
            || (_attachment.Window is not null && _attachment.Enabled));
        var ink = new SolidColorBrush(warning ? Color.FromRgb(244, 190, 114)
            : active ? Color.FromRgb(145, 242, 195) : Color.FromRgb(165, 177, 194));
        StatusDot.Fill = StateLabel.Foreground = ink;
        StatusCard.Background = new SolidColorBrush(active ? Color.FromRgb(20, 38, 31) : Color.FromRgb(23, 28, 38));
        StatusCard.BorderBrush = new SolidColorBrush(active ? Color.FromRgb(47, 81, 64) : Color.FromRgb(44, 52, 67));
    }

    private void ApplyOverlayPreset(CellLayout layout)
    {
        if (!_editing || layout == CellLayout.Custom) return;
        if (_attachment.Profile is { } profile && _attachment.Window is { } target)
        {
            if (!_platform.TryClientRegion(target, out var client)) return;
            if (!(profile with { Layout = layout }).Cell(client).IsValid)
            { ShowStatus("This app window is too small for that aspect ratio."); return; }
            _attachment.SetLayout(layout);
            UpdateAttachedPreview();
        }
        else
        {
            int x = _region.Left + _region.Width / 2, y = _region.Top + _region.Height / 2;
            var monitor = _monitors.FirstOrDefault(m => m.Bounds.Contains(x, y)) ?? _monitors[0];
            var area = layout == CellLayout.MatchClient ? monitor.Bounds
                : ScreenRect.CenterAspect(monitor.Bounds, layout == CellLayout.Center16By9 ? 16d / 9 : 21d / 9);
            if (!area.IsValid) { ShowStatus("This monitor is too small for that aspect ratio."); return; }
            SetRegion(area, persist: false);
        }
        RefreshUi();
    }
}
