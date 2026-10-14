using System.Text.Json;

namespace CursorJail.Core;

public enum PassKey { LeftAlt, MiddleMouse, MouseBack, MouseForward }
public sealed record SavedRegion(string Name, ScreenRect Region);
public sealed record Hotkey(uint Modifiers, uint Key)
{
    public static Hotkey Default => new(3, 0x4C); // Ctrl + Alt + L
    public static Hotkey Emergency => new(3, 0x1B);
    public bool IsValid => (Modifiers & ~7u) == 0 && Key is >= 0x08 and <= 0xFE
        && Key is not (0x10 or 0x11 or 0x12 or 0x5B or 0x5C)
        && !(Key == 0x1B && (Modifiers & 6) == 6) && !(Key == 0x09 && (Modifiers & 1) != 0)
        && (Modifiers != 0 || Key is 0x91 or >= 0x70 and <= 0x87);
}

public sealed class AppSettings
{
    public int Version { get; set; } = 2;
    public ScreenRect? Region { get; set; }
    public Hotkey ToggleHotkey { get; set; } = Hotkey.Default;
    public PassKey PassKey { get; set; }
    public bool EdgeGlow { get; set; } = true;
    public bool TrimWindow { get; set; } = true;
    public List<SavedRegion> Presets { get; set; } = [];
    public List<AttachmentProfile> Attachments { get; set; } = [];
    public string? ActiveAttachmentId { get; set; }
    public bool AttachmentPaused { get; set; }
}

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, IgnoreReadOnlyProperties = true };
    public string? Notice { get; private set; }
    public AppSettings Load()
    {
        if (!File.Exists(path)) return new();
        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions)
                ?? throw new JsonException("Empty settings.");
            if (settings.Version is not (1 or 2)) throw new JsonException("Unsupported settings version.");
            if (settings.Version == 1) { settings.Version = 2; _preserveOriginal = true; }
            if (settings.ToggleHotkey is null || !settings.ToggleHotkey.IsValid || settings.ToggleHotkey == Hotkey.Emergency)
                settings.ToggleHotkey = Hotkey.Default;
            if (!Enum.IsDefined(settings.PassKey)) settings.PassKey = PassKey.LeftAlt;
            if (settings.Region is { IsValid: false }) settings.Region = null;
            settings.Presets = (settings.Presets ?? []).Where(p => p is not null && p.Region.IsValid && !string.IsNullOrWhiteSpace(p.Name))
                .Take(20).Select(p => p with { Name = p.Name.Trim()[..Math.Min(64, p.Name.Trim().Length)] }).ToList();
            var profiles = settings.Attachments ?? [];
            if (profiles.Any(p => p is null || !p.IsValid)) _preserveOriginal = true;
            settings.Attachments = profiles.Where(p => p is { IsValid: true }).DistinctBy(p => p.Id).Take(20).ToList();
            if (!settings.Attachments.Any(p => p.Id == settings.ActiveAttachmentId)) settings.ActiveAttachmentId = null;
            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Notice = "Settings could not be loaded. Defaults are in use; the original file has been kept.";
            // Do not overwrite a malformed or future-version source file on the next save.
            _preserveOriginal = true;
            return new();
        }
    }

    private bool _preserveOriginal;
    public bool Save(AppSettings settings)
    {
        string temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            if (_preserveOriginal && File.Exists(path))
            {
                File.Copy(path, path + ".backup-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"), false);
                _preserveOriginal = false;
            }
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(temporary, path, true);
            Notice = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Notice = "Settings could not be saved. Your current cursor area still works for this session.";
            return false;
        }
    }
}
