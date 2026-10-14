# CursorJail

**[Download CursorJail.exe for Windows x64](https://github.com/Erik900312/CursorJail/releases/download/v0.3.0/CursorJail.exe)** — download and run the app directly.

Prefer a ZIP? **[Download CursorJail-win-x64.zip](https://github.com/Erik900312/CursorJail/releases/download/v0.3.0/CursorJail-win-x64.zip)**, extract it, then run **CursorJail.exe**. GitHub's green **Code → Download ZIP** button downloads the source code and does not include the app executable.

**CursorJail** is a portable Windows desktop utility that keeps your mouse cursor inside a chosen rectangular area. Built for ultrawide and multi-monitor setups, it prevents your pointer from accidentally leaving a game window or clicking onto another monitor.

It uses Windows’ native `ClipCursor` API and runs in the background with its overlay hidden during play, keeping keyboard and mouse focus on your game.

## Key Features

- **Focus-Aware Auto-Attachment:** Locks the cursor when the target window is active and automatically releases it when you `Alt+Tab` or switch apps.
- **Custom & Aspect-Ratio Bounds:** Confine the cursor to the full inner window (excluding standard title bars/borders), presets like **Center 16:9** and **Center 21:9**, or a custom pixel/drag-resized box that scales with the window.
- **Hold to Unlock:** Hold `Left Alt` (or a configured middle/side mouse button) to temporarily free the cursor.
- **Profiles & Manual Mode:** Remember up to 20 apps for automatic reconnection, or use Manual Mode with up to 20 saved regions and an optional visible border.
- **Fail-Safe Controls:** Never locks on startup. Includes an emergency release shortcut, automatic pause on display/session changes, and full system tray controls.

## Quick Start

1. **Run:** Download and launch **`CursorJail.exe`** using the link above, or extract the portable x64 ZIP and run the EXE inside (self-contained .NET 10 build; no runtime installation required).
2. **Select:** Choose an open window from the dropdown and click **Attach to app**, or use **Pick a window on screen**.
3. **Size:** Select **Full app area**, **Center 16:9**, **Center 21:9**, or drag the overlay to a custom size.
4. **Play:** Click **Done**. Cursor locking activates automatically whenever the attached window is in focus.

## Default Shortcuts

| Shortcut | Action |
| :--- | :--- |
| **Ctrl + Alt + L** | Lock/unlock in manual mode; pause/resume auto-locking when attached |
| **Ctrl + Alt + E** | Open or finish area editing |
| **Ctrl + Alt + Esc** | Emergency release cursor and pause automatic locking |
| **Left Alt (Hold)** | Temporarily unlock cursor (configurable to supported mouse buttons) |

## Compatibility & Scope (v0.3.0 Preview)

- **Intended Use:** Designed for **windowed** and **borderless windowed** games and applications.
- **What It Does Not Do:** CursorJail only confines the OS cursor region. It does not detect black bars, change game resolutions, or modify a game's internal camera or edge-scrolling logic.
- **Testing Status:** Version `0.3.0` is a preview release (passing 73 automated specs, native smoke checks, and CI build checks). Exclusive fullscreen titles, specific anti-cheat systems, and physical mixed-DPI monitor setups require further hardware and per-game testing.

## Tech Stack

Built with **C#**, **WPF**, and **.NET 10** (Windows x64).
