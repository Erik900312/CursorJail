using CursorJail.Core;

var tests = new (string Name, Action Run)[]
{
    ("16:9 preset on a 5120 × 1440 ultrawide is centered", () =>
        Equal(new ScreenRect(1280, 0, 3840, 1440), ScreenRect.Preset(new(0, 0, 5120, 1440), RegionPreset.Center16By9))),
    ("16:9 preset fits a portrait monitor", () =>
        Equal(new ScreenRect(0, 656, 1080, 1264), ScreenRect.Preset(new(0, 0, 1080, 1920), RegionPreset.Center16By9))),
    ("half layouts cover odd-width monitors without a gap", () =>
    {
        var monitor = new ScreenRect(-3441, -100, 0, 1340);
        var left = ScreenRect.Preset(monitor, RegionPreset.LeftHalf);
        var right = ScreenRect.Preset(monitor, RegionPreset.RightHalf);
        Equal(left.Right, right.Left); Equal(3441, left.Width + right.Width);
    }),
    ("negative monitor coordinates remain physical pixels", () =>
        Equal(new ScreenRect(-2100, -400, -1100, 200), new ScreenRect(-2200, -500, -1200, 100).Move(100, 100, new(-3440, -1440, 3440, 1440)))),
    ("movement clamps to the desktop without resizing", () =>
        Equal(new ScreenRect(0, 840, 800, 1440), new ScreenRect(100, 100, 900, 700).Move(-500, 5000, new(0, 0, 3440, 1440)))),
    ("resize cannot invert the cage or exceed the desktop", () =>
    {
        var rect = new ScreenRect(100, 100, 900, 700);
        Equal(160, rect.Resize(9999, 0, ResizeEdge.Left, new(0, 0, 3440, 1440)).Width);
        Equal(100, rect.Resize(0, -9999, ResizeEdge.Bottom, new(0, 0, 3440, 1440)).Height);
        Equal(3440, rect.Resize(9999, 0, ResizeEdge.Right, new(0, 0, 3440, 1440)).Right);
    }),
    ("disconnected-screen regions can be recovered", () =>
        Equal(new ScreenRect(0, 0, 800, 600), new ScreenRect(-3000, -500, -2200, 100).FitInside(new(0, 0, 1920, 1080)))),
    ("inverted and overflowing rectangles are rejected", () =>
    {
        Check(!new ScreenRect(int.MaxValue, 0, int.MinValue + 200, 600).IsValid);
        Check(!new ScreenRect(int.MinValue, 0, int.MaxValue, 600).IsValid);
    }),
    ("locking requires a valid, focused, usable target", () =>
    {
        var p = new FakePlatform(); var cage = new JailController(p);
        Check(!cage.Lock(new(0, 0, 800, 600), 2, false));
        Check(!cage.Lock(new(0, 0, 1, 1), 1, false));
        p.Usable = false; Check(!cage.Lock(new(0, 0, 800, 600), 1, false));
        Equal(0, p.Confines);
    }),
    ("watchdog repairs a game clearing its clip", () =>
    {
        var (p, cage) = Locked(); p.Clip = new(0, 0, 3440, 1440); cage.Tick();
        Equal(2, p.Confines); Equal(cage.Region, p.Clip);
    }),
    ("matching clips are not reapplied each tick", () =>
    {
        var (p, cage) = Locked(); for (int i = 0; i < 20; i++) cage.Tick(); Equal(1, p.Confines);
    }),
    ("focus loss wins over watchdog repair", () =>
    {
        var (p, cage) = Locked(); p.Clip = default; p.ForegroundWindow = 2; cage.Tick();
        Equal(JailState.Unlocked, cage.State); Equal(1, p.Confines); Equal(1, p.Releases);
        p.ForegroundWindow = 1; cage.Tick(); Equal(JailState.Unlocked, cage.State);
    }),
    ("foreground hook releases immediately", () =>
    {
        var (p, cage) = Locked(); cage.ForegroundChanged(2); Equal(JailState.Unlocked, cage.State); Equal(1, p.Releases);
    }),
    ("system shortcuts release before repairing a game-cleared clip", () =>
    {
        var (p, cage) = Locked(); p.Escape = true; p.Clip = default; cage.Tick();
        Equal(JailState.Unlocked, cage.State); Equal(1, p.Confines);
    }),
    ("holding the pass key releases then resumes once", () =>
    {
        var (p, cage) = Locked(); p.Pass = true; cage.Tick(); cage.Tick();
        Equal(JailState.Passing, cage.State); Equal(1, p.Releases); Equal(1, p.Confines);
        p.Pass = false; cage.Tick(); Equal(JailState.Locked, cage.State); Equal(2, p.Confines);
    }),
    ("focus changes while passing never reacquire the cage", () =>
    {
        var (p, cage) = Locked(); p.Pass = true; cage.Tick(); p.ForegroundWindow = 2; cage.Tick();
        p.Pass = false; p.ForegroundWindow = 1; cage.Tick(); Equal(JailState.Unlocked, cage.State); Equal(1, p.Confines);
    }),
    ("an Alt-based toggle starts passing until Alt is released", () =>
    {
        var p = new FakePlatform { Pass = true }; var cage = new JailController(p);
        Check(cage.Lock(new(0, 0, 800, 600), 1, false)); Equal(JailState.Passing, cage.State); Equal(0, p.Confines);
        p.Pass = false; cage.Tick(); Equal(1, p.Confines);
    }),
    ("minimized or closed target releases", () =>
    {
        var (p, cage) = Locked(); p.Usable = false; cage.Tick(); Equal(JailState.Unlocked, cage.State); Equal(1, p.Releases);
    }),
    ("snapped cages follow the physical client area", () =>
    {
        var (p, cage) = Locked(true); p.TargetRegion = new(-1000, 100, -200, 700); cage.Tick();
        Equal(p.TargetRegion, cage.Region); Equal(p.TargetRegion, p.Clip);
    }),
    ("invalid snapped area releases instead of clipping tiny bounds", () =>
    {
        var (p, cage) = Locked(true); p.TargetRegion = new(0, 0, 10, 10); cage.Tick(); Equal(JailState.Unlocked, cage.State);
    }),
    ("a region rejected by the desktop observer cannot relock", () =>
    {
        var (p, cage) = Locked(true); cage.Changed += () => { if (cage.IsArmed && cage.Region.Left < 0) cage.Release(); };
        p.TargetRegion = new(-1000, 0, -200, 600); cage.Tick();
        Equal(JailState.Unlocked, cage.State); Equal(1, p.Confines);
    }),
    ("manual cages do not follow target geometry", () =>
    {
        var (p, cage) = Locked(); p.TargetRegion = new(900, 0, 1700, 600); cage.Tick(); Equal(new ScreenRect(0, 0, 800, 600), cage.Region);
    }),
    ("clip failures fail open and disarm", () =>
    {
        var (p, cage) = Locked(); p.ConfineOk = false; p.Clip = default; cage.Tick();
        Equal(JailState.Unlocked, cage.State); Equal(1, p.Releases);
    }),
    ("failed release retries and never reapplies the clip", () =>
    {
        var (p, cage) = Locked(); p.ReleaseOk = false; cage.Release(); Equal(JailState.ReleaseFailed, cage.State);
        cage.Tick(); Equal(1, p.Confines); p.ReleaseOk = true; cage.Tick(); Equal(JailState.Unlocked, cage.State);
    }),
    ("idle shutdown does not clear another application's clip", () =>
    {
        var p = new FakePlatform(); var cage = new JailController(p); cage.Release(); cage.Tick(); Equal(0, p.Releases);
    }),
    ("repeated locking cannot steal a second target", () =>
    {
        var (p, cage) = Locked(); p.ForegroundWindow = 2; Check(!cage.Lock(new(0, 0, 800, 600), 2, false)); Equal((nint)1, cage.Target);
    }),
    ("settings round trip preserves hotkey, negative positions and presets", () => WithTemp(path =>
    {
        var store = new SettingsStore(path); var settings = new AppSettings { Region = new(-2000, -100, -1000, 500), ToggleHotkey = new(0, 0x91), PassKey = PassKey.MiddleMouse, EdgeGlow = false, Presets = [new("Game", new(-2000, -100, -1000, 500))] };
        Check(store.Save(settings)); var loaded = store.Load(); Equal(settings.Region, loaded.Region); Equal(settings.ToggleHotkey, loaded.ToggleHotkey);
        Equal(PassKey.MiddleMouse, loaded.PassKey); Check(!loaded.EdgeGlow); Equal("Game", loaded.Presets.Single().Name);
    })),
    ("corrupt settings are preserved before a replacement is saved", () => WithTemp(path =>
    {
        File.WriteAllText(path, "broken source"); var store = new SettingsStore(path); var settings = store.Load(); Check(store.Notice is not null);
        Check(store.Save(settings)); Equal("broken source", File.ReadAllText(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.backup-*").Single()));
    })),
    ("future settings versions are preserved", () => WithTemp(path =>
    {
        File.WriteAllText(path, "{\"Version\":99}"); var store = new SettingsStore(path); Equal(1, store.Load().Version); Check(store.Notice is not null);
    })),
    ("invalid stored hotkeys and pass keys fall back safely", () => WithTemp(path =>
    {
        File.WriteAllText(path, "{\"ToggleHotkey\":{\"Modifiers\":3,\"Key\":27},\"PassKey\":99,\"Presets\":null}");
        var settings = new SettingsStore(path).Load(); Equal(Hotkey.Default, settings.ToggleHotkey); Equal(PassKey.LeftAlt, settings.PassKey); Equal(0, settings.Presets.Count);
    })),
    ("blank letters cannot become unmodified global shortcuts", () =>
    {
        Check(!new Hotkey(0, 0x4C).IsValid); Check(Hotkey.Default.IsValid); Check(new Hotkey(0, 0x91).IsValid); Check(!new Hotkey(3, 0x11).IsValid);
    })
};

int failures = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failures++; Console.Error.WriteLine($"FAIL {name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} specifications passed.");
return failures == 0 ? 0 : 1;

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}.");
}
static void Check(bool condition) { if (!condition) throw new Exception("Condition was false."); }
static (FakePlatform, JailController) Locked(bool follow = false)
{
    var p = new FakePlatform(); var cage = new JailController(p); Check(cage.Lock(new(0, 0, 800, 600), 1, follow)); return (p, cage);
}
static void WithTemp(Action<string> action)
{
    string folder = Path.Combine(Path.GetTempPath(), "CursorJail-specs-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(folder);
    try { action(Path.Combine(folder, "settings.json")); }
    finally { Directory.Delete(folder, true); }
}

sealed class FakePlatform : ICursorPlatform
{
    public nint ForegroundWindow { get; set; } = 1;
    public bool Usable = true, Escape, Pass, ConfineOk = true, ReleaseOk = true;
    public int Confines, Releases;
    public ScreenRect Clip;
    public ScreenRect TargetRegion = new(0, 0, 800, 600);
    public bool IsEscapeChordDown => Escape;
    public bool IsPassKeyDown => Pass;
    public bool IsUsableWindow(nint window) => Usable;
    public bool TryConfine(ScreenRect region) { Confines++; if (!ConfineOk) return false; Clip = region; return true; }
    public bool TryRelease() { Releases++; if (!ReleaseOk) return false; Clip = default; return true; }
    public bool TryGetClip(out ScreenRect region) { region = Clip; return true; }
    public bool TryGetTargetRegion(nint window, out ScreenRect region) { region = TargetRegion; return true; }
}
