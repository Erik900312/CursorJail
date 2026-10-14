using CursorJail.Core;

internal static class AttachmentSpecs
{
    private static readonly AppIdentity Identity = new(@"C:\Games\Game.exe", "GameClass");
    internal static readonly (string Name, Action Run)[] All =
    [
        ("attachment locks only the selected foreground window", () =>
        {
            var f = New(); f.Platform.ForegroundWindow = 9; f.Auto.Tick(); Equal(0, f.Platform.Confines);
            f.Auto.ForegroundChanged(f.Platform.ForegroundWindow = 1); Equal(JailState.Locked, f.Jail.State);
        }),
        ("attached game focus returns automatically reacquire", () =>
        {
            var f = New(locked: true); f.Auto.ForegroundChanged(f.Platform.ForegroundWindow = 9);
            Equal(JailState.Unlocked, f.Jail.State); Equal(1, f.Platform.Releases);
            f.Auto.ForegroundChanged(f.Platform.ForegroundWindow = 1); Equal(2, f.Platform.Confines);
        }),
        ("emergency pause survives focus changes and watchdog ticks", () =>
        {
            var f = New(locked: true); f.Auto.Pause("Emergency release");
            f.Auto.ForegroundChanged(f.Platform.ForegroundWindow = 9); f.Auto.ForegroundChanged(f.Platform.ForegroundWindow = 1);
            for (int i = 0; i < 10; i++) f.Auto.Tick();
            Equal(1, f.Platform.Confines); Equal(JailState.Unlocked, f.Jail.State); Check(!f.Auto.Enabled);
            f.Auto.Resume(); f.Auto.Tick(); Equal(2, f.Platform.Confines);
        }),
        ("pause disables automatic mode before notifying release observers", () =>
        {
            var f = New(locked: true); f.Jail.Changed += () => f.Auto.ForegroundChanged(1);
            f.Auto.Pause(); Equal(1, f.Platform.Confines); Equal(JailState.Unlocked, f.Jail.State);
        }),
        ("restored profiles never lock on startup ticks alone", () =>
        {
            var f = New(); f.Auto.Restore(Profile(), paused: false);
            for (int i = 0; i < 10; i++) f.Auto.Tick(); Equal(0, f.Platform.Confines);
            f.Auto.ForegroundChanged(1); Equal(1, f.Platform.Confines);
        }),
        ("restored paused profiles require explicit resume", () =>
        {
            var f = New(); f.Auto.Restore(Profile(), paused: true); f.Auto.ForegroundChanged(1); f.Auto.Tick(); Equal(0, f.Platform.Confines);
            f.Auto.Resume(); f.Auto.Tick(); Equal(1, f.Platform.Confines);
        }),
        ("restoring a different profile releases the old clip", () =>
        {
            var f = New(locked: true); f.Auto.Restore(Profile() with { Name = "Another profile" }, false);
            Equal(JailState.Unlocked, f.Jail.State); Equal(1, f.Platform.Releases); f.Auto.Tick(); Equal(1, f.Platform.Confines);
        }),
        ("an executable basename in another directory cannot match", () =>
        {
            var f = New(); f.Auto.Restore(Profile(), false);
            f.Platform.Windows[1] = f.Platform.Windows[1] with { Identity = new(@"C:\Unrelated\Game.exe", "GameClass") };
            f.Auto.ForegroundChanged(1); Equal(0, f.Platform.Confines); Check(f.Auto.Window is null);
        }),
        ("a different window class cannot match the remembered game", () =>
        {
            var f = New(); f.Auto.Restore(Profile(), false);
            f.Platform.Windows[1] = f.Platform.Windows[1] with { Identity = new(@"C:\Games\Game.exe", "LauncherClass") };
            f.Auto.ForegroundChanged(1); Equal(0, f.Platform.Confines);
        }),
        ("Windows executable path comparisons ignore case", () =>
        {
            Check(Identity.Matches(new(@"c:/games/GAME.EXE", "GameClass")));
        }),
        ("a live attachment never switches to a sibling window", () =>
        {
            var f = New(locked: true); f.Platform.Windows[2] = new(2, Identity, new(0, 0, 1000, 800));
            f.Auto.ForegroundChanged(f.Platform.ForegroundWindow = 2);
            Equal((nint)1, f.Auto.Window!.Handle); Equal(1, f.Platform.Confines); Equal(JailState.Unlocked, f.Jail.State);
        }),
        ("closed games can reattach to a new matching process", () =>
        {
            var f = New(locked: true); f.Platform.Windows.Remove(1);
            f.Platform.Windows[2] = new(22, Identity, new(100, 100, 1100, 900));
            f.Auto.ForegroundChanged(f.Platform.ForegroundWindow = 2);
            Equal((nint)2, f.Auto.Window!.Handle); Equal((nint)2, f.Jail.Target); Equal(2, f.Platform.Confines);
        }),
        ("recycled handles from another process release and cannot relock", () =>
        {
            var f = New(locked: true); f.Platform.Windows[1] = new(99, new(@"C:\Other\Other.exe", "OtherClass"), new(0, 0, 1000, 800));
            f.Auto.Tick(); Equal(JailState.Unlocked, f.Jail.State); Equal(1, f.Platform.Confines); Check(f.Auto.Window is null);
        }),
        ("minimizing keeps the binding but releases the cage", () =>
        {
            var f = New(locked: true); f.Platform.Windows[1] = f.Platform.Windows[1] with { Visible = false }; f.Auto.Tick();
            Equal(JailState.Unlocked, f.Jail.State); Equal((nint)1, f.Auto.Window!.Handle);
            f.Platform.Windows[1] = f.Platform.Windows[1] with { Visible = true }; f.Auto.Tick(); Equal(2, f.Platform.Confines);
        }),
        ("game close releases without clearing unrelated clips on later ticks", () =>
        {
            var f = New(locked: true); f.Platform.Windows.Remove(1); f.Auto.Tick(); Equal(1, f.Platform.Releases);
            for (int i = 0; i < 5; i++) f.Auto.Tick(); Equal(1, f.Platform.Releases);
        }),
        ("attached cells follow negative physical client coordinates", () =>
        {
            var f = New(locked: true); f.Platform.Windows[1] = f.Platform.Windows[1] with { Client = new(-3000, -500, -1000, 1100) };
            f.Auto.Tick(); Equal(f.Platform.Windows[1].Client, f.Platform.Clip);
        }),
        ("custom cell margins survive moving and resizing the game", () =>
        {
            var f = New(); Check(f.Auto.SetCustomCell(new(200, 200, 1000, 800)));
            f.Platform.Windows[1] = f.Platform.Windows[1] with { Client = new(-2000, 400, 0, 2000) };
            f.Auto.Tick(); Equal(new ScreenRect(-1800, 600, -200, 1800), f.Platform.Clip); Equal(CellLayout.Custom, f.Auto.Profile!.Layout);
        }),
        ("editing cannot put an attached cell outside the client area", () =>
        {
            var f = New(); Check(!f.Auto.SetCustomCell(new(0, 0, 1000, 800))); Equal(CellLayout.MatchClient, f.Auto.Profile!.Layout);
        }),
        ("center 16:9 fits inside a full-monitor borderless canvas", () =>
        {
            var f = New(); f.Platform.Windows[1] = f.Platform.Windows[1] with { Client = new(0, 0, 5120, 1440) };
            f.Auto.SetLayout(CellLayout.Center16By9); f.Auto.Tick(); Equal(new ScreenRect(1280, 0, 3840, 1440), f.Platform.Clip);
        }),
        ("center 21:9 keeps its aspect when the window resizes", () =>
        {
            var f = New(); f.Platform.Windows[1] = f.Platform.Windows[1] with { Client = new(0, 0, 5120, 1440) };
            f.Auto.SetLayout(CellLayout.Center21By9); f.Auto.Tick(); Equal(new ScreenRect(880, 0, 4240, 1440), f.Platform.Clip);
            f.Platform.Windows[1] = f.Platform.Windows[1] with { Client = new(0, 0, 3440, 1440) }; f.Auto.Tick();
            Equal(new ScreenRect(40, 0, 3400, 1440), f.Platform.Clip);
        }),
        ("borderless client coordinates receive no fixed title-bar trim", () =>
        {
            var f = New(); f.Platform.Windows[1] = f.Platform.Windows[1] with { Client = new(0, 0, 2560, 1440) };
            f.Auto.Tick(); Equal(0, f.Platform.Clip.Top); Equal(1440, f.Platform.Clip.Height);
        }),
        ("too-small windows release until their cell fits again", () =>
        {
            var f = New(locked: true); f.Platform.Windows[1] = f.Platform.Windows[1] with { Client = new(0, 0, 100, 80) }; f.Auto.Tick();
            Equal(JailState.Unlocked, f.Jail.State);
            f.Platform.Windows[1] = f.Platform.Windows[1] with { Client = new(0, 0, 1000, 800) }; f.Auto.Tick(); Equal(2, f.Platform.Confines);
        }),
        ("off-desktop cells cannot be confined", () =>
        {
            var f = New(locked: true); f.Platform.Available = false; f.Auto.Tick(); Equal(JailState.Unlocked, f.Jail.State); Equal(1, f.Platform.Confines);
        }),
        ("native cursor failures pause rather than repeatedly reacquire", () =>
        {
            var f = New(); f.Platform.ConfineOk = false; f.Auto.Tick(); f.Auto.Tick();
            Check(!f.Auto.Enabled); Equal(1, f.Platform.Confines); Equal(JailState.Unlocked, f.Jail.State);
        }),
        ("failed release pauses automatic mode and only retries release", () =>
        {
            var f = New(locked: true); f.Platform.ReleaseOk = false;
            f.Auto.ForegroundChanged(f.Platform.ForegroundWindow = 9); f.Auto.Tick(); Check(!f.Auto.Enabled);
            Equal(JailState.ReleaseFailed, f.Jail.State); f.Platform.ReleaseOk = true; f.Auto.Tick();
            Equal(JailState.Unlocked, f.Jail.State); f.Auto.ForegroundChanged(f.Platform.ForegroundWindow = 1); f.Auto.Tick(); Equal(1, f.Platform.Confines);
        }),
        ("the parole key passes temporarily within a focused game", () =>
        {
            var f = New(locked: true); f.Platform.Pass = true; f.Auto.Tick(); Equal(JailState.Passing, f.Jail.State); Equal(1, f.Platform.Releases);
            f.Platform.Pass = false; f.Auto.Tick(); Equal(2, f.Platform.Confines); Check(f.Auto.Enabled);
        }),
        ("side-clicking another app while passing stays free until the game returns", () =>
        {
            var f = New(locked: true); f.Platform.Pass = true; f.Auto.Tick();
            f.Auto.ForegroundChanged(f.Platform.ForegroundWindow = 9); f.Platform.Pass = false; f.Auto.Tick(); Equal(JailState.Unlocked, f.Jail.State);
            f.Auto.ForegroundChanged(f.Platform.ForegroundWindow = 1); Equal(2, f.Platform.Confines);
        }),
        ("holding Alt during activation starts on parole", () =>
        {
            var f = New(); f.Platform.Pass = true; f.Auto.Tick(); Equal(JailState.Passing, f.Jail.State); Equal(0, f.Platform.Confines);
            f.Platform.Pass = false; f.Auto.Tick(); Equal(1, f.Platform.Confines);
        }),
        ("system task-switching keys gate automatic lock until released", () =>
        {
            var f = New(locked: true); f.Platform.Escape = true; f.Auto.Tick(); Equal(JailState.Unlocked, f.Jail.State);
            Check(f.Auto.Enabled); f.Platform.Escape = false; f.Auto.Tick(); Equal(2, f.Platform.Confines);
        }),
        ("editing releases and suppresses the automatic cage", () =>
        {
            var f = New(locked: true); f.Auto.SetEditing(true); f.Auto.Tick(); f.Auto.ForegroundChanged(1);
            Equal(JailState.Unlocked, f.Jail.State); Equal(1, f.Platform.Confines);
            f.Auto.SetEditing(false); f.Auto.Tick(); Equal(2, f.Platform.Confines);
        }),
        ("native window dragging releases until move end", () =>
        {
            var f = New(locked: true); f.Auto.WindowMove(1, true); f.Auto.Tick(); Equal(JailState.Unlocked, f.Jail.State);
            Equal(1, f.Platform.Confines); f.Auto.WindowMove(1, false); Equal(2, f.Platform.Confines);
        }),
        ("unrelated window moves do not change the active cage", () =>
        {
            var f = New(locked: true); f.Auto.WindowMove(9, true); f.Auto.Tick(); Equal(JailState.Locked, f.Jail.State); Equal(0, f.Platform.Releases);
        }),
        ("a focus change during clip inspection cannot repair the old lock", () =>
        {
            var f = New(locked: true); f.Platform.Clip = default; f.Platform.OnGetClip = () => f.Platform.ForegroundWindow = 9;
            f.Auto.Tick(); Equal(JailState.Unlocked, f.Jail.State); Equal(1, f.Platform.Confines); Check(f.Auto.Enabled);
        }),
        ("automatic watchdog repair still requires the target focus", () =>
        {
            var f = New(locked: true); f.Platform.Clip = default; f.Auto.Tick(); Equal(2, f.Platform.Confines);
            f.Platform.Clip = default; f.Auto.ForegroundChanged(f.Platform.ForegroundWindow = 9); f.Auto.Tick(); Equal(2, f.Platform.Confines);
        }),
        ("missing escape or focus services prevent automatic confinement", () =>
        {
            var f = New(); f.Auto.SetReady(false); f.Auto.Tick(); Equal(0, f.Platform.Confines); Check(!f.Auto.Enabled);
        }),
        ("detach returns to manual mode without later automatic locking", () =>
        {
            var f = New(locked: true); f.Auto.Detach(); f.Auto.ForegroundChanged(1); f.Auto.Tick();
            Check(f.Auto.Profile is null); Equal(JailState.Unlocked, f.Jail.State); Equal(1, f.Platform.Confines);
        }),
        ("cancelled cell changes can restore the previous margins", () =>
        {
            var f = New(); var before = f.Auto.Profile!; Check(f.Auto.SetCustomCell(new(200, 200, 1000, 800)));
            f.Auto.RestoreCell(before); Equal(CellLayout.MatchClient, f.Auto.Profile!.Layout); Equal(default(CellMargins), f.Auto.Profile.Margins);
        }),
        ("non-finite or inverted margin profiles are rejected", () =>
        {
            Check(!new CellMargins(double.NaN, 0, 0, 0).IsValid); Check(!new CellMargins(.6, 0, .5, 0).IsValid);
            Check(!new CellMargins(-.1, 0, 0, 0).IsValid); Check(new CellMargins(.1, .2, .1, .2).IsValid);
        }),
        ("v1 settings migrate with all existing choices and a source backup", () => Temp(path =>
        {
            const string source = "{\"Version\":1,\"PassKey\":1,\"EdgeGlow\":false,\"Region\":{\"Left\":-1000,\"Top\":0,\"Right\":0,\"Bottom\":800},\"Presets\":[{\"Name\":\"Old cage\",\"Region\":{\"Left\":0,\"Top\":0,\"Right\":800,\"Bottom\":600}}]}";
            File.WriteAllText(path, source); var store = new SettingsStore(path); var settings = store.Load();
            Equal(2, settings.Version); Equal(PassKey.MiddleMouse, settings.PassKey); Check(!settings.EdgeGlow);
            Equal(-1000, settings.Region!.Value.Left); Equal("Old cage", settings.Presets.Single().Name);
            Check(store.Save(settings)); Equal(source, File.ReadAllText(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.backup-*").Single()));
        })),
        ("attachments round-trip without persisting HWND or process IDs", () => Temp(path =>
        {
            var profile = Profile() with { Layout = CellLayout.Custom, Margins = new(.1, .2, .1, .2) };
            var settings = new AppSettings { Attachments = [profile], ActiveAttachmentId = profile.Id, AttachmentPaused = true, PassKey = PassKey.MouseBack };
            var store = new SettingsStore(path); Check(store.Save(settings)); var loaded = store.Load();
            Equal(profile, loaded.Attachments.Single()); Equal(profile.Id, loaded.ActiveAttachmentId); Check(loaded.AttachmentPaused);
            Equal(PassKey.MouseBack, loaded.PassKey); string json = File.ReadAllText(path); Check(!json.Contains("ProcessId") && !json.Contains("Handle"));
        })),
        ("invalid stored app identities are removed without losing their source", () => Temp(path =>
        {
            var settings = new AppSettings { Attachments = [Profile() with { Identity = new("Game.exe", "GameClass") }] };
            var store = new SettingsStore(path); Check(store.Save(settings)); settings = store.Load(); Equal(0, settings.Attachments.Count);
            Check(store.Save(settings)); Check(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.backup-*").Length == 1);
        })),
        ("v1 custom Ctrl+Alt+E toggle is preserved during migration", () => Temp(path =>
        {
            File.WriteAllText(path, "{\"Version\":1,\"ToggleHotkey\":{\"Modifiers\":3,\"Key\":69}}");
            var settings = new SettingsStore(path).Load(); Equal(new Hotkey(3, 0x45), settings.ToggleHotkey);
        }))
    ];

    private static AttachmentProfile Profile() => new("0123456789abcdef0123456789abcdef", "Game", Identity, CellLayout.MatchClient, default);
    private static (Platform Platform, JailController Jail, AttachmentController Auto) New(bool locked = false)
    {
        var platform = new Platform(); var jail = new JailController(platform); var auto = new AttachmentController(platform, jail);
        auto.SetReady(true); Check(auto.Attach(Profile(), new(1, 1, Identity))); if (locked) auto.Tick();
        return (platform, jail, auto);
    }
    private static void Check(bool value) { if (!value) throw new Exception("Condition was false."); }
    private static void Equal<T>(T expected, T actual)
    { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; actual {actual}."); }
    private static void Temp(Action<string> action)
    {
        string folder = Path.Combine(Path.GetTempPath(), "CursorJail-attachment-specs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try { action(Path.Combine(folder, "settings.json")); }
        finally { Directory.Delete(folder, true); }
    }
    private sealed record Win(uint ProcessId, AppIdentity Identity, ScreenRect Client, bool Visible = true);
    private sealed class Platform : IAttachmentPlatform
    {
        internal readonly Dictionary<nint, Win> Windows = new() { [1] = new(1, Identity, new(100, 100, 1100, 900)) };
        public nint ForegroundWindow { get; set; } = 1;
        internal bool Escape, Pass, Available = true, ConfineOk = true, ReleaseOk = true;
        internal int Confines, Releases;
        internal ScreenRect Clip;
        internal Action? OnGetClip;
        public bool IsEscapeChordDown => Escape;
        public bool IsPassKeyDown => Pass;
        public bool IsUsableWindow(nint window) => Windows.TryGetValue(window, out var w) && w.Visible && w.Client.IsValid;
        public bool TryConfine(ScreenRect region) { Confines++; if (!ConfineOk) return false; Clip = region; return true; }
        public bool TryRelease() { Releases++; if (!ReleaseOk) return false; Clip = default; return true; }
        public bool TryGetClip(out ScreenRect region) { OnGetClip?.Invoke(); region = Clip; return true; }
        public bool TryGetTargetRegion(nint window, out ScreenRect region)
        { region = Windows.TryGetValue(window, out var w) ? w.Client : default; return region.IsValid; }
        public bool TryIdentifyWindow(nint handle, out AttachedWindow window)
        {
            window = null!;
            if (!Windows.TryGetValue(handle, out var w) || !IsUsableWindow(handle)) return false;
            window = new(handle, w.ProcessId, w.Identity); return true;
        }
        public bool IsWindowInstance(AttachedWindow window) => Windows.TryGetValue(window.Handle, out var w) && w.ProcessId == window.ProcessId;
        public bool TryClientRegion(AttachedWindow window, out ScreenRect client)
        { client = IsWindowInstance(window) && IsUsableWindow(window.Handle) ? Windows[window.Handle].Client : default; return client.IsValid; }
        public bool IsRegionAvailable(ScreenRect region) => Available;
    }
}
