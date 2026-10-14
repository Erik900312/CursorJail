namespace CursorJail.Core;

// Explicit attachment is the opt-in authority for automatic reacquisition.
// JailController remains the sole owner of ClipCursor and its release/retry state.
public sealed class AttachmentController(IAttachmentPlatform platform, JailController jail)
{
    private bool _ready;
    private bool _requireFreshFocus;
    private bool _moving;
    private nint _lastForeground;
    private ScreenRect? _lastCell;
    public AttachmentProfile? Profile { get; private set; }
    public AttachedWindow? Window { get; private set; }
    public bool Enabled { get; private set; }
    public bool Editing { get; private set; }
    public ScreenRect? Cell { get; private set; }
    public string Message { get; private set; } = "Attach a window to lock automatically while it has focus.";
    public event Action? Changed;

    public void Restore(AttachmentProfile profile, bool paused)
    {
        if (!profile.IsValid) return;
        jail.Release();
        if (jail.State == JailState.ReleaseFailed) return;
        Profile = profile;
        Window = null;
        Editing = _moving = false;
        Cell = _lastCell = null;
        _lastForeground = 0;
        Enabled = !paused;
        _requireFreshFocus = true; // Startup itself must never acquire the cursor.
        Report(paused ? "Automatic locking is paused. Resume when ready." : "Waiting for the remembered app to gain focus.");
        Changed?.Invoke();
    }

    public bool Attach(AttachmentProfile profile, AttachedWindow window)
    {
        if (!profile.IsValid || !profile.Identity.Matches(window.Identity) || !platform.TryClientRegion(window, out _)) return false;
        jail.Release();
        if (jail.State == JailState.ReleaseFailed) return false;
        Profile = profile;
        Window = window;
        Enabled = true;
        _requireFreshFocus = false;
        _moving = false;
        Cell = _lastCell = null;
        Report("Attached. Cursor locking activates when this window has focus.");
        return true;
    }

    public void SetReady(bool ready)
    {
        if (!ready && _ready && Profile is not null) Pause("Automatic locking paused: release shortcuts or focus monitoring are unavailable.");
        _ready = ready;
    }

    public void ForegroundChanged(nint foreground)
    {
        jail.ForegroundChanged(foreground); // Release synchronously, before any identity lookup.
        if (Profile is null || !Enabled) return;
        _requireFreshFocus = false;
        ResolveWindow(foreground);
        _lastForeground = foreground;
        Tick();
    }

    private void ResolveWindow(nint foreground)
    {
        if (Window is not null && platform.IsWindowInstance(Window)) return; // Never switch to a sibling window while the chosen one lives.
        Window = null;
        _moving = false;
        if (Profile is not null && platform.TryIdentifyWindow(foreground, out var candidate) && Profile.Identity.Matches(candidate.Identity))
            Window = candidate;
    }

    public void Tick()
    {
        if (Profile is null) return;
        if (jail.State == JailState.ReleaseFailed)
        {
            Enabled = false;
            jail.Tick();
            Report("Automatic locking paused after a release error. Resume explicitly when ready.");
            return;
        }
        if (!Enabled || !_ready || _requireFreshFocus || Editing) return;
        nint foreground = platform.ForegroundWindow;
        if (Window is not null && !platform.IsWindowInstance(Window))
        {
            jail.Release("Released. The attached window closed.");
            Window = null;
            _moving = false;
            _lastForeground = 0;
        }
        if (foreground != _lastForeground)
        {
            ResolveWindow(foreground);
            _lastForeground = foreground;
        }
        if (Window is null || foreground != Window.Handle || _moving || platform.IsEscapeChordDown
            || !platform.TryClientRegion(Window, out var client))
        {
            if (jail.IsArmed) jail.Release("Released. The attached window is not ready or focused.");
            Report(_moving ? "Window is moving. Cursor locking resumes when the move ends." : "Attached. Waiting for this app to have focus.");
            return;
        }
        var cell = Profile.Cell(client);
        if (!cell.IsValid || !platform.IsRegionAvailable(cell))
        {
            if (jail.IsArmed) jail.Release("Released. The cursor area is too small or outside the desktop.");
            Report("Waiting. Move the app onto the desktop or enlarge its cursor area.");
            return;
        }
        Cell = cell;
        if (jail.IsArmed)
        {
            jail.UpdateRegion(cell);
            jail.Tick();
        }
        else if (!jail.Lock(cell, Window.Handle, followWindow: false))
        {
            if (jail.State != JailState.ReleaseFailed && !TargetReady()) { Report("Attached. Waiting for this app to have focus."); return; }
            Pause("Automatic locking paused. " + jail.Message);
            return;
        }
        if (jail.State == JailState.Unlocked || jail.State == JailState.ReleaseFailed)
        {
            if (jail.State == JailState.Unlocked && !TargetReady()) { Report("Attached. Waiting for this app to have focus."); return; }
            Pause("Automatic locking paused after a Windows cursor error. Resume explicitly when ready.");
            return;
        }
        Report(jail.State == JailState.Passing ? "Temporarily unlocked while the key or button is held." : "Attached and locked. Switch apps to release; return to lock again.");
        if (_lastCell != Cell) { _lastCell = Cell; Changed?.Invoke(); }
    }

    public void Pause(string message = "Automatic locking paused. Resume explicitly when ready.")
    {
        Enabled = false; // Disable before release: a foreground event cannot undo an emergency escape.
        jail.Release(message);
        Report(message);
    }

    public void Resume()
    {
        if (Profile is null || jail.State == JailState.ReleaseFailed) return;
        Enabled = true;
        _requireFreshFocus = false;
        _lastForeground = 0;
        Report("Automatic locking enabled. Focus the attached app to play.");
    }

    public void Detach()
    {
        Pause("Detached. Manual locking is ready.");
        Profile = null;
        Window = null;
        Cell = _lastCell = null;
        Editing = _moving = false;
        Changed?.Invoke();
    }

    public void SetEditing(bool editing)
    {
        Editing = editing;
        if (editing) jail.Release("Adjusting the cursor area. Cursor is free.");
        Report(editing ? "Adjust the cursor area, then choose Done or click back into the app." : Enabled ? "Cursor area ready. Focus the attached app to lock." : "Automatic locking is paused. Resume when ready.");
    }

    public void SetLayout(CellLayout layout)
    {
        if (Profile is null || !Enum.IsDefined(layout) || layout == CellLayout.Custom) return;
        Profile = Profile with { Layout = layout, Margins = default };
        Changed?.Invoke();
    }

    public bool SetCustomCell(ScreenRect cell)
    {
        if (Profile is null || Window is null || !platform.TryClientRegion(Window, out var client) || !cell.IsValid
            || cell.Left < client.Left || cell.Top < client.Top || cell.Right > client.Right || cell.Bottom > client.Bottom) return false;
        var margins = CellMargins.FromCell(cell, client);
        if (!margins.IsValid) return false;
        Profile = Profile with { Layout = CellLayout.Custom, Margins = margins };
        Cell = cell;
        Changed?.Invoke();
        return true;
    }

    public void RestoreCell(AttachmentProfile original)
    {
        if (Profile?.Id != original.Id || !original.IsValid) return;
        Profile = Profile with { Layout = original.Layout, Margins = original.Margins };
        Changed?.Invoke();
    }

    public void WindowMove(nint window, bool moving)
    {
        if (Window?.Handle != window || Profile is null) return;
        _moving = moving;
        if (moving && jail.IsArmed) jail.Release("Released while moving the game window.");
        if (!moving) Tick();
    }

    private void Report(string message)
    {
        if (Message == message) return;
        Message = message;
        Changed?.Invoke();
    }

    private bool TargetReady() => Window is not null && platform.ForegroundWindow == Window.Handle
        && !platform.IsEscapeChordDown && platform.TryClientRegion(Window, out _);
}
