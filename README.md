# CursorJail

<img src="src/CursorJail/Assets/CursorJail.svg" width="72" alt="CursorJail icon" />

A visual cursor cage for Windows. Drag a rectangle over a windowed game, snap to its client area, or choose an ultrawide layout. Lock the cage to keep your cursor inside it.

Built with **C#, WPF and .NET 10 LTS**. Uses Windows `ClipCursor` for confinement, a click-through overlay, and passive foreground events. No game-process injection or cursor teleporting.

<img src="docs/CursorJail.png" width="520" alt="CursorJail control panel" />

## Run

Download `CursorJail-win-x64.zip` from this repository's Releases, extract it, and open **CursorJail.exe**. The portable package includes .NET; no installation or administrator access is needed. Windows 10/11, x64. The executable is currently unsigned.

1. Choose a game window and **Snap**, or select a monitor layout.
2. **Edit cage on screen** lets you move the rectangle and resize any edge or corner. **Done editing** hides the frame.
3. Focus your game and press **Ctrl + Alt + L**, or click **Lock cage in 3s** and focus the game during the countdown.
4. Hold **Left Alt** to pass through temporarily. Release it to lock again while the same window remains focused. Middle mouse is available instead.
5. **Ctrl + Alt + Esc** always requests release. Alt+Tab, Windows keys, Task Manager shortcuts, focus changes, sleep, and display changes release the cage too.

After a focus change, use the toggle to lock again. It does not automatically reacquire the cursor. Starting the app never locks the mouse.

The panel's **—** button hides it to the tray. Double-click the tray icon to reopen it. **✕** or **Exit CursorJail** releases the cage and exits. The tray menu includes saved cages and release controls.

## Features

- Drag to move; resize with eight visible handles and all four edges.
- Snap to open windows. **Trim title bar & borders** uses the actual client rectangle rather than guessed border sizes. Snapped cages follow the window; dragging switches to a fixed manual region.
- Centered 16:9, left half, right half, and full monitor layouts, including monitors at negative desktop coordinates.
- Exact position and size in physical pixels. Minimum size: 160 × 100.
- Custom toggle shortcut; permanent emergency shortcut. Locking is disabled if essential shortcuts or foreground monitoring cannot register.
- A faint border appears within 20 pixels of an edge, or can be hidden entirely. The locked overlay lets clicks through.
- A 50 ms watchdog repairs a game's cleared cursor clip only while the target remains focused. The timer stops while idle.
- Up to 20 named cages; settings saved atomically in `%LOCALAPPDATA%\CursorJail\settings.json`. Corrupt or newer-version settings are backed up before replacement.
- Per-monitor V2 DPI awareness, single-instance behavior, and release on normal exit and handled failures.

## Build and verify

Requires Windows and the .NET 10 SDK. No third-party runtime packages.

```powershell
dotnet build CursorJail.slnx --configuration Release
dotnet run --project tests/CursorJail.Specs --configuration Release --no-build
# Native HWND/DPI/style checks and off-screen UI renders; never locks the cursor.
src/CursorJail/bin/Release/net10.0-windows/CursorJail.exe --smoke-test artifacts/smoke
# Build, verify and publish a portable ZIP:
pwsh -NoProfile -File tools/Publish.ps1
# Optional Windows ARM64 package:
pwsh -NoProfile -File tools/Publish.ps1 -Runtime win-arm64
```

GitHub Actions builds on Windows, runs the core specifications, and uploads the self-contained x64 package. Regenerate the icon with `tools/New-Icon.ps1` after changing its drawing.

## Implementation

`CursorJail.Core` contains physical-pixel geometry, the confinement state machine and settings storage. `CursorJail` contains the WPF panel/overlay, Win32 interop, shortcuts, foreground hook, tray menu and lifecycle handling. `CursorJail.Specs` exercises safety and geometry with a deterministic platform double. Native smoke checks validate the real WPF HWND, overlay bounds/styles, monitor enumeration and DPI context.

Windows owns the cursor as a shared resource. CursorJail releases only clips it has acquired. Focus and escape checks always run before watchdog repair. A failed release enters a retry state and cannot re-lock. See the [ClipCursor documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-clipcursor).

WPF initializes DPI from the manifest; Windows Forms is used for the tray icon only. The WinForms-only WFO0003 analyzer is therefore suppressed in the WPF project.

## Practical limits

Intended for windowed and borderless games. Exclusive fullscreen overlays and games that continually replace the OS clip may behave differently. Raw-input camera movement can remain independent of the desktop cursor. Passive Win32 APIs avoid injection, but compatibility with a particular game's anti-cheat is not certified. Core and native smoke checks do not substitute for testing in your game, or on a physical mixed-DPI multi-monitor setup.
