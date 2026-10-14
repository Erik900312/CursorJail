# CursorJail working agreement

Implement directly in this project. Do not invoke Gemini, Flash, Antigravity or a worker delegation workflow. This follows the owner's explicit instruction for CursorJail.

CursorJail is a Windows desktop utility built with C#, .NET 10 LTS and WPF. Keep core geometry in physical desktop pixels. Negative screen coordinates are valid. Use native ClipCursor, never mouse teleporting or game injection.

Safety invariants:

- App startup never confines the cursor.
- Focus loss, system shortcuts, session/display changes and exit release the cage.
- Focus and escape checks precede watchdog repair.
- No automatic lock reacquisition after a focus change.
- Registration failures disable locking if escape controls or focus monitoring are unavailable.
- A failed release cannot reapply a clip.
- Do not clear clips owned by other apps while CursorJail is idle.
- Preserve malformed or future settings before replacement.

Verify changes with `dotnet build CursorJail.slnx --configuration Release` and `dotnet run --project tests/CursorJail.Specs --configuration Release --no-build`. WPF changes also require `CursorJail.exe --smoke-test artifacts/smoke` and visual inspection of the resulting panel renders. See `docs/MANUAL-TESTS.md` for hardware/game acceptance checks.

Build outputs and local verification artifacts are ignored. Do not commit credentials, package caches or published executables. Distribution binaries belong in GitHub Releases or Actions artifacts.
