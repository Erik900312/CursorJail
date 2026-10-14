# Desktop acceptance checks

Run these on Windows before a release intended for broader use. Always keep the emergency shortcut available. Automated smoke checks do not confine the real mouse.

- Launch the portable executable without a .NET runtime installed; confirm the panel and tray icon appear. A second launch should report the existing instance.
- Edit a cage. Move it and resize every edge/corner. Check its four pixel fields, including negative monitor origins. Small cages must stop at 160 × 100.
- Snap to a window with native title bars, toggle trim, and compare the rectangle against the actual client area. Custom title bars drawn by an app remain part of its client area.
- Focus the target and toggle the cage. Confirm clicks reach the window and the pointer stops at the selected edges. Repeat with the three-second button and cancel the countdown using both global shortcuts.
- Confirm Ctrl+Alt+Esc releases immediately. Alt+Tab, Win, Ctrl+Shift+Esc, target minimize/close, and focus change must release and stay unlocked after returning to the target.
- Hold/release Left Alt and middle mouse. Check temporary passage, then change focus while holding; releasing the key must leave the cage unlocked.
- While locked, simulate a game clearing `ClipCursor`. Repair must occur only while the same target remains focused.
- Move/resize a snapped target. Confirm the cage follows its client area; moving it outside available desktop space releases it.
- Check 100%, 125%, 150% and mixed-DPI monitors. The displayed boundary and cursor stop must coincide, including during a drag between displays.
- Disconnect a display, lock/unlock the Windows session, and suspend/resume. Each must release rather than automatically reacquire the cage.
- Reserve a toggle shortcut in another app. Changing the shortcut should fail with an explanation and keep the prior binding. Reserve the emergency binding: CursorJail must disable locking.
- Save/reload/delete cages, restart, and verify settings persist. Leave a numeric field blank: applying dimensions must explain the missing input rather than turning it into zero.
- Hide/reopen from the tray, exit while locked, and confirm cursor freedom and removal of the tray icon and hotkeys.

## Verification performed during initial implementation

31 core specifications, release compilation, native HWND/style/bounds/DPI checks, global shortcut registration/replacement, tray icon loading, and off-screen UI rendering at 100%, 150% and compact height. In-game behavior, exclusive fullscreen, physical mixed-DPI transitions, ARM64 execution and anti-cheat compatibility require the checks above on suitable hardware.
