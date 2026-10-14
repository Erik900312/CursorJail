namespace CursorJail.Core;

// All geometry uses physical desktop pixels, including negative monitor origins.
public readonly record struct ScreenRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool IsValid => Right > Left && Bottom > Top && Width >= 160 && Height >= 100;
    public bool Contains(int x, int y) => x >= Left && x < Right && y >= Top && y < Bottom;

    public ScreenRect FitInside(ScreenRect bounds)
    {
        int width = Math.Clamp(Width, Math.Min(160, bounds.Width), bounds.Width);
        int height = Math.Clamp(Height, Math.Min(100, bounds.Height), bounds.Height);
        int left = Math.Clamp(Left, bounds.Left, bounds.Right - width);
        int top = Math.Clamp(Top, bounds.Top, bounds.Bottom - height);
        return new(left, top, left + width, top + height);
    }

    public ScreenRect Move(int dx, int dy, ScreenRect bounds)
    {
        int left = Math.Clamp(Left + dx, bounds.Left, bounds.Right - Width);
        int top = Math.Clamp(Top + dy, bounds.Top, bounds.Bottom - Height);
        return new(left, top, left + Width, top + Height);
    }

    public ScreenRect Resize(int dx, int dy, ResizeEdge edges, ScreenRect bounds)
    {
        int left = edges.HasFlag(ResizeEdge.Left) ? Math.Clamp(Left + dx, bounds.Left, Right - 160) : Left;
        int top = edges.HasFlag(ResizeEdge.Top) ? Math.Clamp(Top + dy, bounds.Top, Bottom - 100) : Top;
        int right = edges.HasFlag(ResizeEdge.Right) ? Math.Clamp(Right + dx, left + 160, bounds.Right) : Right;
        int bottom = edges.HasFlag(ResizeEdge.Bottom) ? Math.Clamp(Bottom + dy, top + 100, bounds.Bottom) : Bottom;
        return new(left, top, right, bottom);
    }

    public static ScreenRect Preset(ScreenRect monitor, RegionPreset preset)
    {
        int width = monitor.Width;
        int height = monitor.Height;
        return preset switch
        {
            RegionPreset.LeftHalf => new(monitor.Left, monitor.Top, monitor.Left + width / 2, monitor.Bottom),
            RegionPreset.RightHalf => new(monitor.Left + width / 2, monitor.Top, monitor.Right, monitor.Bottom),
            RegionPreset.Center16By9 => Center(monitor, Math.Min(width, (int)Math.Round(height * 16d / 9)),
                Math.Min(height, (int)Math.Round(width * 9d / 16))),
            _ => monitor
        };
    }

    public static ScreenRect Center(ScreenRect monitor, int width, int height)
    {
        int x = monitor.Left + (monitor.Width - width) / 2;
        int y = monitor.Top + (monitor.Height - height) / 2;
        return new(x, y, x + width, y + height);
    }

    public static ScreenRect CenterAspect(ScreenRect bounds, double aspect)
    {
        int width = Math.Min(bounds.Width, (int)Math.Round(bounds.Height * aspect));
        int height = Math.Min(bounds.Height, (int)Math.Round(bounds.Width / aspect));
        return Center(bounds, width, height);
    }
}

[Flags]
public enum ResizeEdge { None = 0, Left = 1, Top = 2, Right = 4, Bottom = 8 }
public enum RegionPreset { Center16By9, LeftHalf, RightHalf, FullMonitor }
