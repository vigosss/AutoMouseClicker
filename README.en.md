<div align="center">

[简体中文](README.md) | [English](README.en.md)

# 🎯 Ming AutoClicker — Windows Auto Clicker &amp; Macro Recorder

![License](https://img.shields.io/github/license/vigosss/AutoMouseClicker)
![Stars](https://img.shields.io/github/stars/vigosss/AutoMouseClicker)
![Language](https://img.shields.io/github/languages/top/vigosss/AutoMouseClicker)
![.NET](https://img.shields.io/badge/.NET-8.0-blue)

**Windows Auto Clicker, Mouse &amp; Keyboard Macro Recorder, and Image Recognition Automation Tool**

Ming AutoClicker is a WPF and OpenCV desktop automation tool with continuous clicking, mouse and keyboard recording/playback, editable macros, image recognition, coordinate clicks, and per-item global hotkeys.

</div>

---

## Features

### 🖱️ Auto Clicker

- Left, middle, and right mouse buttons
- Configurable interval from 10 ms to 60,000 ms
- Live click counter
- Configurable global start/stop hotkey (`F8` by default)

### ⏺️ Mouse &amp; Keyboard Recorder

- Records mouse movement, clicks, wheel input, and keyboard actions
- Pause/resume, playback speed, finite/infinite loops, and loop intervals
- Rename, optimize, search, import, and export recordings
- Per-recording global hotkeys
- Virtual-desktop coordinate mapping when the display layout changes

### 📋 Macro Editor

- Create, edit, duplicate, delete, and reorder macros
- Find Image, Wait, and Mouse Position Click actions
- Optional step notes for long workflows
- Fixed-count or infinite loops with a configurable interval
- Portable JSON storage with atomic writes

### 🔍 Image Recognition

- Template matching powered by Emgu CV (OpenCV)
- Cached templates, grayscale images, and scaled variants
- Fast default scale search and optional adaptive scaling from 0.5x to 2.0x
- Adjustable similarity threshold (80% by default)
- Full-screen region capture or local image upload
- “Wait until found” polling mode
- Multi-monitor, negative-coordinate, and DPI-aware capture
- Visual match results with score, scale, threshold, method, and elapsed time

### 🌐 Languages

- Simplified Chinese and English
- Follows the Windows display language on first launch
- Instant language switching from Settings without restarting

---

## Requirements and Installation

- Windows 10 or later
- Download the latest ZIP package from [GitHub Releases](https://github.com/vigosss/AutoMouseClicker/releases)
- Extract the complete archive, then run `Ming-AutoClicker.exe`
- Global hotkeys or simulated mouse input may require running the application as administrator in some environments

The application checks GitHub Releases on startup. When a required update is available, it downloads and installs the update from inside the app.

---

## Usage

### Auto Clicker

1. Open the **🖱 Auto Clicker** tab.
2. Choose the left, middle, or right mouse button.
3. Set the click interval in milliseconds.
4. Move the pointer to the target position.
5. Press the configured global hotkey (`F8` by default) to start, and press it again to stop.

### Mouse Macros

1. Open the **📋 Mouse Macros** tab.
2. Select **＋ Create Macro**, then choose **✏️ Edit**.
3. Add Find Image, Wait, or Mouse Position Click actions.
4. Configure each action and reorder the steps as needed.
5. Save the macro.
6. Select it and click **▶ Start**, or press the configured global hotkey.

### Find Image Action

| Option | Description |
|---|---|
| Capture / Upload | Capture a screen region or import a local template image |
| Match threshold | Minimum similarity required for a successful match |
| Operation | Left-click or right-click after a match |
| X/Y offset | Offset from the center of the matched image |
| Wait until found | Keep searching until the image appears or the operation times out |
| Adaptive scaling | Search a wider scale range when DPI or window scaling changes |

### Settings and Hotkeys

Open **⚙ Settings** in the lower-right corner to choose **Follow system**, **简体中文**, or **English**, and to configure the global start/stop hotkey.

Supported hotkeys are `F1`–`F12`, or `Ctrl` / `Alt` / `Shift` combined with a letter or number. Letters and numbers cannot be used alone to prevent accidental activation while typing.

| Key | Action |
|---|---|
| Configured global hotkey (`F8` by default) | Start or stop the current auto-clicker/macro mode |
| `ESC` | Close or cancel capture, coordinate picker, and match-result overlays |

---

## Data and Compatibility

- Macros are saved as JSON files and remain compatible across language changes.
- Template images are stored in the application screenshot data folder.
- Language and hotkey preferences are stored per Windows user and survive application updates.
- Macro names, step notes, filenames, and release notes are shown exactly as written and are not automatically translated.

---

## Changelog

### v0.6.0

- Added global mouse and keyboard recording and playback
- Added pause, playback speed, finite/infinite loops, and loop intervals
- Added recording management, search, rename, optimization, and JSON import/export
- Added per-item global hotkeys for macros and recordings with safe task switching
- Added virtual-desktop coordinate mapping when the display layout changes
- Improved atomic recording persistence, corrupt-file backup, and input release on stop

- Added complete Simplified Chinese and English interfaces
- Added system-language detection and instant language switching
- Combined language and global-hotkey controls into one Settings window

### v0.5.0

- Added a configurable global start/stop hotkey
- Added F1–F12 and Ctrl/Alt/Shift + letter or number combinations
- Persisted hotkey settings per Windows user
- Added safe fallback behavior for hotkey conflicts and registration failures

### v0.4.0

- Improved template-matching speed, caching, scale handling, and diagnostics
- Added step notes, image import, multi-monitor support, and DPI-aware coordinates
- Improved macro persistence, ordering, cleanup, execution safety, and single-instance behavior

---

## License

Released under the [MIT License](LICENSE).

<div align="center">

If this project helps you, please consider giving it a ⭐ star.

Made with ❤️ by [vigosss](https://github.com/vigosss)

</div>
