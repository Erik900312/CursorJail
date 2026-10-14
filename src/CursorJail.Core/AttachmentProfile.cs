namespace CursorJail.Core;

public sealed record AppIdentity(string ExecutablePath, string WindowClass)
{
    public bool IsValid => !string.IsNullOrWhiteSpace(ExecutablePath) && !string.IsNullOrWhiteSpace(WindowClass)
        && ExecutablePath.Length <= 32768 && WindowClass.Length <= 256
        && (ExecutablePath.StartsWith(@"\\", StringComparison.Ordinal)
            || (ExecutablePath.Length > 3 && char.IsLetter(ExecutablePath[0]) && ExecutablePath[1] == ':' && ExecutablePath[2] is '\\' or '/'));
    public bool Matches(AppIdentity other) => IsValid && other.IsValid
        && ExecutablePath.Replace('/', '\\').Equals(other.ExecutablePath.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase)
        && WindowClass.Equals(other.WindowClass, StringComparison.Ordinal);
}

public enum CellLayout { MatchClient, Center16By9, Center21By9, Custom }

// Fractions of the client area: preserved when the window moves, resizes or changes DPI.
public readonly record struct CellMargins(double Left, double Top, double Right, double Bottom)
{
    public bool IsValid => double.IsFinite(Left) && double.IsFinite(Top) && double.IsFinite(Right) && double.IsFinite(Bottom)
        && Left >= 0 && Top >= 0 && Right >= 0 && Bottom >= 0 && Left + Right < 1 && Top + Bottom < 1;

    public ScreenRect Apply(ScreenRect client) => new(
        client.Left + (int)Math.Round(client.Width * Left), client.Top + (int)Math.Round(client.Height * Top),
        client.Right - (int)Math.Round(client.Width * Right), client.Bottom - (int)Math.Round(client.Height * Bottom));

    public static CellMargins FromCell(ScreenRect cell, ScreenRect client) => new(
        (double)(cell.Left - client.Left) / client.Width, (double)(cell.Top - client.Top) / client.Height,
        (double)(client.Right - cell.Right) / client.Width, (double)(client.Bottom - cell.Bottom) / client.Height);
}

public sealed record AttachmentProfile(string Id, string Name, AppIdentity Identity, CellLayout Layout, CellMargins Margins)
{
    public bool IsValid => Guid.TryParse(Id, out _) && !string.IsNullOrWhiteSpace(Name) && Name.Length <= 128
        && Identity is { IsValid: true } && Enum.IsDefined(Layout) && Margins.IsValid;

    public ScreenRect Cell(ScreenRect client) => Layout switch
    {
        CellLayout.Center16By9 => ScreenRect.CenterAspect(client, 16d / 9),
        CellLayout.Center21By9 => ScreenRect.CenterAspect(client, 21d / 9),
        CellLayout.Custom => Margins.Apply(client),
        _ => client
    };
}

public sealed record AttachedWindow(nint Handle, uint ProcessId, AppIdentity Identity, string? InstanceClass = null);

public interface IAttachmentPlatform : ICursorPlatform
{
    bool TryIdentifyWindow(nint handle, out AttachedWindow window);
    bool IsWindowInstance(AttachedWindow window);
    bool TryClientRegion(AttachedWindow window, out ScreenRect client);
    bool IsRegionAvailable(ScreenRect region);
}
