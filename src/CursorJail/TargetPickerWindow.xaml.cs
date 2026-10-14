using System.Windows.Interop;
using CursorJail.Native;

namespace CursorJail;

public partial class TargetPickerWindow : Window
{
    private readonly ScreenRect _desktop;
    private bool _finished;
    private HwndSource? _source;
    internal event Action<nint>? Selected;
    internal TargetPickerWindow(ScreenRect desktop)
    {
        _desktop = desktop;
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
            _source?.AddHook(Hook);
            ApplyBounds();
        };
        Loaded += (_, _) => { ApplyBounds(); Activate(); };
        Deactivated += (_, _) => { if (!_finished) Finish(0); };
        Closed += (_, _) => { if (_source is { IsDisposed: false }) _source.RemoveHook(Hook); };
    }
    private void ApplyBounds() => NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, NativeMethods.HwndTopmost,
        _desktop.Left, _desktop.Top, _desktop.Width, _desktop.Height, NativeMethods.SwpNoActivate);
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == NativeMethods.WmDpiChanged) Dispatcher.BeginInvoke(new Action(ApplyBounds));
        return 0;
    }
    private void Pick(object sender, MouseButtonEventArgs e)
    {
        if (!NativeMethods.GetCursorPos(out var point)) { Finish(0); return; }
        _finished = true;
        Hide();
        nint target = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(point), 2);
        Close();
        Selected?.Invoke(target);
    }
    private void KeyPressed(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; Finish(0); } }
    internal void Cancel() => Finish(0);
    private void Finish(nint target)
    {
        if (_finished) return;
        _finished = true;
        Close();
        Selected?.Invoke(target);
    }
}
