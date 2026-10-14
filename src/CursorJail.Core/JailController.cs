namespace CursorJail.Core;

public enum JailState { Unlocked, Locked, Passing, ReleaseFailed }

public interface ICursorPlatform
{
    nint ForegroundWindow { get; }
    bool IsUsableWindow(nint window);
    bool IsEscapeChordDown { get; }
    bool IsPassKeyDown { get; }
    bool TryConfine(ScreenRect region);
    bool TryRelease();
    bool TryGetClip(out ScreenRect region);
    bool TryGetTargetRegion(nint window, out ScreenRect region);
}

public sealed class JailController(ICursorPlatform platform)
{
    private readonly object _gate = new();
    private bool _ownsClip;
    private bool _followWindow;
    private string _releaseMessage = "Released. Your cursor is free.";
    public JailState State { get; private set; }
    public ScreenRect Region { get; private set; }
    public nint Target { get; private set; }
    public string Message { get; private set; } = "Ready when you are.";
    public bool IsArmed => State is JailState.Locked or JailState.Passing;
    public event Action? Changed;

    public bool Lock(ScreenRect region, nint target, bool followWindow)
    {
        lock (_gate) return LockCore(region, target, followWindow);
    }

    private bool LockCore(ScreenRect region, nint target, bool followWindow)
    {
        if (State != JailState.Unlocked) return false;
        if (!region.IsValid || target == 0 || !platform.IsUsableWindow(target) || platform.ForegroundWindow != target)
            return Report("Focus a game or window, then use the toggle shortcut.");
        if (platform.IsEscapeChordDown)
            return Report("Release the task-switching keys before locking.");
        Region = region;
        Target = target;
        _followWindow = followWindow;
        // The hotkey itself may include Alt. Start in pass-through until it is released.
        if (platform.IsPassKeyDown)
        {
            State = JailState.Passing;
            Message = "Passing through. Release the pass key to lock.";
            Changed?.Invoke();
            return true;
        }
        return Confine();
    }

    public void Tick()
    {
        lock (_gate) TickCore();
    }

    public void UpdateRegion(ScreenRect region)
    {
        lock (_gate)
        {
            if (!IsArmed || Region == region) return;
            if (!region.IsValid) { Release("Released. Invalid cursor area."); return; }
            Region = region;
            Changed?.Invoke();
        }
    }

    private void TickCore()
    {
        if (State == JailState.ReleaseFailed)
        {
            Release(_releaseMessage);
            return;
        }
        if (!IsArmed) return;
        // Focus / escape must be checked BEFORE the watchdog is allowed to reapply ClipCursor.
        if (platform.IsEscapeChordDown || platform.ForegroundWindow != Target || !platform.IsUsableWindow(Target))
        {
            Release("Released. Focus changed or a system shortcut was pressed.");
            return;
        }
        if (platform.IsPassKeyDown)
        {
            if (State == JailState.Locked && ReleaseClip())
            {
                State = JailState.Passing;
                Message = "Passing through. Release the pass key to lock.";
                Changed?.Invoke();
            }
            return;
        }
        if (_followWindow)
        {
            if (!platform.TryGetTargetRegion(Target, out var region) || !region.IsValid)
            {
                Release("Released. The target window is unavailable.");
                return;
            }
            if (Region != region)
            {
                Region = region;
                Changed?.Invoke();
                if (!IsArmed) return; // A subscriber may reject an off-screen region and release.
            }
        }
        if (State == JailState.Passing || !platform.TryGetClip(out var clip) || clip != Region)
            Confine();
    }

    public void ForegroundChanged(nint foreground)
    {
        lock (_gate)
        {
            if (IsArmed && foreground != Target)
                Release("Released. Focus moved to another window.");
        }
    }

    public void Release(string message = "Released. Your cursor is free.")
    {
        lock (_gate) ReleaseCore(message);
    }

    private void ReleaseCore(string message)
    {
        _releaseMessage = message;
        if (!ReleaseClip()) return;
        State = JailState.Unlocked;
        Target = 0;
        Message = message;
        Changed?.Invoke();
    }

    private bool Confine()
    {
        if (platform.ForegroundWindow != Target || platform.IsEscapeChordDown || !platform.IsUsableWindow(Target))
        { Release("Released. Focus changed or a system shortcut was pressed."); return false; }
        if (!platform.TryConfine(Region))
        {
            Release("Windows could not lock the cursor. Try again in a normal desktop window.");
            return false;
        }
        _ownsClip = true;
        State = JailState.Locked;
        Message = "Locked. Your cursor stays inside the selected area.";
        Changed?.Invoke();
        return true;
    }

    private bool ReleaseClip()
    {
        if (!_ownsClip) return true;
        if (!platform.TryRelease())
        {
            State = JailState.ReleaseFailed;
            Message = "Windows could not release the cursor. Retrying; use Ctrl + Alt + Esc.";
            Changed?.Invoke();
            return false;
        }
        _ownsClip = false;
        return true;
    }

    private bool Report(string message)
    {
        Message = message;
        Changed?.Invoke();
        return false;
    }
}
