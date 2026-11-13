# Regression tests

Run on Windows with the SDK selected by the repository's `global.json`:

```powershell
dotnet build Ming-AutoClicker.sln -c Release
dotnet run --project Tests/Ming-AutoClicker.Tests.csproj -c Release --no-build
```

This standalone test executable has no additional test-framework dependencies. It returns a nonzero exit code on failure and is run by the build workflow. Native input is replaced with a fake sender: tests do not move the real pointer, press keys, or register desktop hotkeys.

Release lifecycle checks verify that exit waits for recorded input cleanup, timed-out exit preparation can be retried, and a failed macro load preserves its possible template images during save/disposal. They use fake input and isolated temporary data directories.

To regenerate the ten checked-in documentation screenshots, explicitly run `dotnet run --project Tests/Ming-AutoClicker.Tests.csproj -c Release --no-build -- --docs-screenshots` from the source checkout. It renders the real windows off screen with separate form/page demonstration data and a code-generated confirmation-button template, writing Chinese images to `docs/images/` and English images to `docs/images/en/`. It never shows windows, captures the desktop, starts playback, or reads/writes user recordings or macros; ordinary regression runs do not update these images.

The `--input-layout-only` check renders the editor's loop fields in Chinese and English at 700/800 DIP widths and 96/120/144/192 DPI. It verifies that header controls fit without overlaps and values fit without horizontal scrolling before focus and after caret changes. It runs off screen and saves `editor-input-initial-<locale>-<width>-<dpi>dpi.png` artifacts.

The suite checks input ordering, wheel signs and overflow bounds, partial failures and cancellation cleanup, macro execution, legacy JSON, cloning, hotkey conflict matching, and editor validation/save/cancel behavior. It also renders both editor panels in Chinese and English at 700×500 and 800×600 without showing a window. Images are written under `Tests/bin/Release/net10.0-windows/artifacts/`.

Recording tests use temporary JSON storage, fake capture/input, and a controllable monotonic clock. They cover progress resets between recordings, stale callbacks, enabled-action counts and loops, pause/resume timing, stopping and cleanup failures, empty recordings, and parameter validation. The recording page is rendered in Chinese and English at 644/784 DIP content widths (the minimum/default main window widths) and 96/120/144/192 DPI, with assertions that the two-row footer remains stable and its controls do not overlap. DPI changes invalidate all generated layout before establishing each scenario's baseline.

The `--pixel-theme-only` check renders the actual main window at 680×520 and 820×620, settings, update and confirmation windows in both languages at 96/120/144/192 DPI. It verifies that the embedded Fusion Pixel font resolves without a system-font fallback, icon pixels retain their original 24-unit coordinates, labels fit and controls do not overlap. The windows stay off screen: no native handles, tray icons or desktop hooks are created. Screenshots use the `pixel-` prefix under the test artifacts directory. Run this check with `dotnet Tests/bin/Release/net10.0-windows/Ming-AutoClicker.Tests.dll --pixel-theme-only` after building.

After building, run just the recording checks with `dotnet run --project Tests/Ming-AutoClicker.Tests.csproj -c Release --no-build -- --recording-only`.

The separate `--recording-popup-only` check briefly shows a test window and opens its actual recording menus in both languages. It verifies the native popup, light command icons, dark background and red delete icon/label, then closes the test windows. It never injects keyboard or mouse input and is excluded from default off-screen tests.

Image-matching coverage includes automatic intermediate scales, independently rendered WPF DPI samples, color distractors, missing targets, duplicate targets (including transparent padding), ignored areas, alternate appearances, cache invalidation, physical coordinates, scaled click anchors, fresh-frame verification, cancellation, and deadlines. The executor uses injected image frames and mouse input; these tests never click the desktop. Invalid configuration containers are rejected before editor cloning, and legacy wait/offset semantics are retained.

Benchmark lines compare fixed generated 1080p frames against the original three-scale and sixteen-scale strategies, and report automatic matching on small icons and 4K frames. They are local measurements, not universal latency guarantees. Original-size matching can be faster in the old early-exit strategy; the new strategy checks competing targets and verifies appearances. DPI fixtures render real WPF controls off screen, but do not replace tests in actual target applications.

Before release, verify these scenarios interactively on Windows:

- Record a short mouse/keyboard sequence, pause and resume it, then replay it once and in a loop. Check the elapsed time excludes recording pauses, stop playback using the main button and the global/per-recording hotkeys, and switch recordings to confirm progress resets. Verify the card menu, two-row footer, speed slider and recording pause/stop explanations in both languages.
- Scroll a target application vertically and horizontally, both at the current pointer position and at picked coordinates on a second monitor with negative coordinates.
- Use Click → Ctrl+A → Ctrl+C and Tab / Enter / arrow-key actions in an ordinary application; check numeric keypad behavior with the intended Num Lock setting.
- Start with a Ctrl-based control hotkey and confirm the first Scroll action waits for Ctrl to be released instead of zooming.
- Confirm global, macro, and recording hotkey conflicts report the correct step before minimizing; changing the control hotkey allows execution.
- Stop while waiting for a physical key to be released and during a repeating mixed-action macro. Confirm no keys remain held and no subsequent steps execute.
- Open saved older macros, save/copy a mixed macro, and reopen it to confirm all steps remain present.
- Capture the same target at different display DPI and browser zoom levels, move its window between monitors, and verify the highlighted click point before running the macro.
- Bind a window and a relative client region; move/resize it, then minimize, cover, close, or duplicate it. Explicit scope failures must never search other windows.
- Test duplicate buttons, transparent padding, ignored changing numbers, and alternate appearances. Verify the candidate order before explicitly selecting the Nth target.
- Stop during a long image search, and move or remove the target before the final verification. No subsequent click or macro step should occur after failure or cancellation.

These interactive target-application and physical-hotkey checks are not replaced by the fake-input suite.

To run the focused window-visibility regression after building, use:

```powershell
dotnet Tests/bin/Release/net10.0-windows/Ming-AutoClicker.Tests.dll --window-visibility-only
```

This optional check briefly displays a synthetic target in a separate process, an owner window, a modal editor, and the matching preview. It verifies that capturing hides both application windows without ending the modal editor, that actual screen pixels expose the target, and that the preview's stop button can receive clicks. It checks normal and maximized editors and closes its test windows automatically. It does not inject mouse or keyboard input and is excluded from the default off-screen regression run.
