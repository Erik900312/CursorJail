using System.Windows.Interop;
using CursorJail.Native;

namespace CursorJail;

public partial class OverlayWindow : Window
{
    private nint _handle;
    private HwndSource? _source;
    private ScreenRect _region;
    private ScreenRect _desktop;
    private ScreenRect _dragRegion;
    private NativeMethods.Point _dragStart;
    private ResizeEdge _edges;
    private bool _dragging;
    private bool _editing;
    internal event Action<ScreenRect>? RegionChanged;
    internal event Action? EditFinished;

    public OverlayWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            _handle = new WindowInteropHelper(this).Handle;
            _source = HwndSource.FromHwnd(_handle);
            _source?.AddHook(Hook);
            ApplyStyles();
            ApplyBounds();
        };
        Closed += (_, _) => { if (_source is { IsDisposed: false }) _source.RemoveHook(Hook); };
    }

    internal nint Handle => new WindowInteropHelper(this).EnsureHandle();
    internal void SetRegion(ScreenRect region, ScreenRect desktop)
    {
        _region = region;
        _desktop = desktop;
        Dimensions.Text = $"{region.Width:N0} × {region.Height:N0} px";
        if (_handle != 0) ApplyBounds();
    }
    internal void SetMode(bool editing, bool visible)
    {
        if (!editing && _dragging) FinishDrag();
        _editing = editing;
        Canvas.Background = editing ? new SolidColorBrush(Color.FromArgb(20, 131, 239, 180)) : Brushes.Transparent;
        Hint.Visibility = Handles.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        Frame.BorderThickness = new Thickness(editing ? 2 : 1);
        Frame.BorderBrush = editing ? new SolidColorBrush(Color.FromRgb(145, 242, 195)) : Brushes.Transparent;
        Canvas.IsHitTestVisible = editing;
        ApplyStyles();
        if (visible)
        {
            if (!IsVisible) Show();
            ApplyBounds();
        }
        else Hide();
    }
    internal void UpdateGlow()
    {
        if (_editing || !IsVisible || !NativeMethods.GetCursorPos(out var cursor)) return;
        int distance = Math.Min(Math.Min(cursor.X - _region.Left, _region.Right - 1 - cursor.X),
            Math.Min(cursor.Y - _region.Top, _region.Bottom - 1 - cursor.Y));
        Frame.BorderBrush = distance is >= 0 and <= 20
            ? new SolidColorBrush(Color.FromArgb(38, 145, 242, 195)) : Brushes.Transparent;
    }
    private void ApplyStyles()
    {
        if (_handle == 0) return;
        long style = (long)NativeMethods.GetWindowLongPtr(_handle, NativeMethods.GwlExStyle);
        style |= NativeMethods.ToolWindow | NativeMethods.NoActivate;
        style = _editing ? style & ~NativeMethods.Transparent : style | NativeMethods.Transparent;
        NativeMethods.SetWindowLongPtr(_handle, NativeMethods.GwlExStyle, (nint)style);
    }
    private void ApplyBounds()
    {
        if (_handle == 0 || !_region.IsValid) return;
        NativeMethods.SetWindowPos(_handle, NativeMethods.HwndTopmost, _region.Left, _region.Top,
            _region.Width, _region.Height, NativeMethods.SwpNoActivate | NativeMethods.SwpNoOwnerZOrder);
    }
    private nint Hook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == NativeMethods.WmMouseActivate) { handled = true; return 3; } // MA_NOACTIVATE
        if (message == NativeMethods.WmDpiChanged)
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ApplyBounds));
        return 0;
    }
    private ResizeEdge EdgesAt(System.Windows.Point point)
    {
        ResizeEdge edges = ResizeEdge.None;
        if (point.X < 14) edges |= ResizeEdge.Left;
        if (point.X > ActualWidth - 14) edges |= ResizeEdge.Right;
        if (point.Y < 14) edges |= ResizeEdge.Top;
        if (point.Y > ActualHeight - 14) edges |= ResizeEdge.Bottom;
        return edges;
    }
    private void BeginDrag(object sender, MouseButtonEventArgs e)
    {
        if (!_editing || !NativeMethods.GetCursorPos(out _dragStart)) return;
        _edges = EdgesAt(e.GetPosition(this));
        _dragRegion = _region;
        _dragging = Canvas.CaptureMouse();
        e.Handled = true;
    }
    private void Drag(object sender, MouseEventArgs e)
    {
        if (!_editing) return;
        if (_dragging && NativeMethods.GetCursorPos(out var cursor))
        {
            int dx = cursor.X - _dragStart.X, dy = cursor.Y - _dragStart.Y;
            var next = _edges == ResizeEdge.None ? _dragRegion.Move(dx, dy, _desktop) : _dragRegion.Resize(dx, dy, _edges, _desktop);
            RegionChanged?.Invoke(next);
        }
        var edges = _dragging ? _edges : EdgesAt(e.GetPosition(this));
        Cursor = edges switch
        {
            ResizeEdge.Left or ResizeEdge.Right => Cursors.SizeWE,
            ResizeEdge.Top or ResizeEdge.Bottom => Cursors.SizeNS,
            (ResizeEdge.Left | ResizeEdge.Top) or (ResizeEdge.Right | ResizeEdge.Bottom) => Cursors.SizeNWSE,
            (ResizeEdge.Left | ResizeEdge.Bottom) or (ResizeEdge.Right | ResizeEdge.Top) => Cursors.SizeNESW,
            _ => Cursors.SizeAll
        };
    }
    private void EndDrag(object sender, MouseButtonEventArgs e) => FinishDrag();
    private void CaptureLost(object sender, MouseEventArgs e) { if (_dragging) FinishDrag(); }
    private void FinishDrag()
    {
        _dragging = false;
        Canvas.ReleaseMouseCapture();
        EditFinished?.Invoke();
    }
}
