# CursorJail working agreement

Implement directly in this project. Do not invoke Gemini, Flash, Antigravity or a worker delegation workflow. This follows the owner's explicit instruction for CursorJail.

## Complete delivery

The owner uses "commit and push", "update GitHub", "publish" and equivalent requests to mean complete delivery of the current app update. These requests authorize the necessary version bump, build, release and updates to existing delivery locations. A source push alone does not finish the task. Follow any explicit narrower instruction from the owner.

For app changes, finish all of the following before reporting completion:

- Commit and push the intended changes to the active GitHub branch, preserving unrelated work. Verify the remote commit and zero upstream divergence.
- Keep the assembly version, visible app version, release tag, release notes and README download links consistent.
- Build and run the required verification below. Publish the standalone Windows executable from the committed app source, then smoke-test that exact executable and inspect its panel renders.
- Create or update the GitHub release for that version with the tested executable and its SHA256 checksum. Prefer one executable download plus checksums. Verify the uploaded asset digest matches the tested binary and the public download returns HTTP 200.
- Update the current local distribution in `dist`, any executable linked to the owner during the task, and the existing Downloads delivery location (`%USERPROFILE%\Downloads\CursorJail.exe` and a copy named for the new version). Verify their hashes against the released binary. Keep deliberately historical files and release versions intact; do not overwrite an old version's filename with a new version.
- Check GitHub Actions for the release commit. Wait for completion without frequent polling, inspect failures, and resolve them before claiming the update is fully verified. State any actual blocker or remaining manual acceptance gap explicitly.

Documentation-only or instruction-only changes require committing/pushing and relevant checks, but do not require a new app version or rebuilding unchanged executables. Do not describe the task as fully delivered while a required destination still has the previous app version. Final delivery must link to the current downloadable app and report verification accurately.

CursorJail is a Windows desktop utility built with C#, .NET 10 LTS and WPF. Keep core geometry in physical desktop pixels. Negative screen coordinates are valid. Use native ClipCursor, never mouse teleporting or game injection.

Safety invariants:

- App startup never confines the cursor.
- Focus loss, system shortcuts, session/display changes and exit release the cage.
- Focus and escape checks precede watchdog repair.
- Manual cages never reacquire after focus loss. Explicit app attachments may reacquire only for the identified app/window while automatic mode is enabled.
- Emergency release and explicit pause disable automatic reacquisition until the user resumes. App startup itself never acquires a clip.
- Registration failures disable locking if escape controls or focus monitoring are unavailable.
- A failed release cannot reapply a clip.
- Do not clear clips owned by other apps while CursorJail is idle.
- Preserve malformed or future settings before replacement.

Verify changes with `dotnet build CursorJail.slnx --configuration Release` and `dotnet run --project tests/CursorJail.Specs --configuration Release --no-build`. WPF changes also require `CursorJail.exe --smoke-test artifacts/smoke` and visual inspection of the resulting panel renders. See `docs/MANUAL-TESTS.md` for hardware/game acceptance checks.

Build outputs and local verification artifacts are ignored. Do not commit credentials, package caches or published executables. Distribution binaries belong in GitHub Releases or Actions artifacts.
