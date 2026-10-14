# Desktop acceptance checks

Run these on Windows before a release intended for broader use. Always keep the emergency shortcut available. Automated smoke checks do not confine the real mouse.

- Launch the portable executable without a .NET runtime installed; confirm the panel and tray icon appear. A second launch should report the existing instance.
- Edit a cage. Move it and resize every edge/corner. Check its four pixel fields, including negative monitor origins. Small cages must stop at 160 × 100.
- Attach through the icon dropdown and crosshair picker. The selection click must choose the underlying root window, never the picker, without clicking a game control. Esc/Alt+Tab cancel selection.
- Test native-windowed and borderless canvases: exclude real native chrome, preserve full borderless height. Custom client-drawn title bars require margins.
- Match canvas, Center 16:9 and Center 21:9 on 5120 × 1440 should yield 5120 × 1440, 2560 × 1440 and 3360 × 1440 cells. Check negative monitor origins.
- Resize an attached cell: preserve the attachment and keep it within the client area. Custom margins scale; aspect presets retain their aspect. Done/Cancel must fit at 160 × 100.
- Done/clicking back into the game commits and hides the editor. Esc/Cancel restores the initial cell and pauses. Ctrl+Alt+E opens/finishes editing when available; only explicit editing/picking may take foreground focus.
- Focus the target and toggle the cage. Confirm clicks reach the window and the pointer stops at the selected edges. Repeat with the three-second button and cancel the countdown using both global shortcuts.
- Automatic mode: Alt+Tab/Win/Ctrl+Shift+Esc, minimize and focus loss release; returning to the selected game reacquires. Manual mode remains released until toggled.
- Emergency release and explicit pause must stay paused across focus changes and restarts until Resume. Normal enabled exit/restart must remember automatic mode, but startup alone must never clip.
- Hold/release all four Parole keys. Side-click another app while holding, release there, and stay free until the game regains focus.
- While locked, simulate a game clearing `ClipCursor`. Repair must occur only while the same target remains focused.
- Native game-window dragging releases until move end. Programmatic movement/resizing follows the client area. Off-desktop or too-small cells release.
- Close/relaunch the remembered game and focus it to reattach by executable/class. A live selected window must not silently switch to a sibling. Only one remembered profile is active.
- Check 100%, 125%, 150% and mixed-DPI monitors. The displayed boundary and cursor stop must coincide, including during a drag between displays.
- Disconnect a display, lock/unlock the Windows session, and suspend/resume. Each must release rather than automatically reacquire the cage.
- Reserve a toggle in another app: fail with an explanation and keep the prior binding. Reserve emergency release: disable locking. A conflicting optional editor key leaves its button available.
- Upgrade v1 settings with presets/custom shortcuts, including Ctrl+Alt+E. Preserve them and create a source backup. An existing Ctrl+Alt+E toggle takes priority over the editor shortcut.
- Save/reload/delete cages and remembered profiles, restart, and verify persistence. HWNDs/process IDs must not appear in settings. Blank numeric input must not become zero.
- Hide/reopen from the tray, exit while locked, and confirm cursor freedom and removal of the tray icon and hotkeys.

## Verification performed for v0.2.0

73 deterministic specifications cover legacy safety, automatic focus/return, emergency pause, window identity/instance/relaunch, relative margins, aspect presets, editing, window moves, migration and shortcut preservation. Release compilation passes with zero warnings.

Native smoke checks cover HWND/DPI/bounds/styles, focus/move hooks, executable path queries, external client coordinates, icons, shortcut replacement/conflicts, hidden picker hit testing and enabled preference on normal close. Manual/attached panels render at 100%, 150% and compact height; editor renders include 160 × 100. The packaged executable and GitHub Windows CI are also checked before release.

Actual game input, physical mixed-DPI transitions, Fullscreen Optimizations, exclusive fullscreen, ARM64 execution and individual anti-cheat compatibility remain hardware/game acceptance work.
