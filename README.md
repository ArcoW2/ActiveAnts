# ActiveAnts

Draws an animated "marching ants" dashed border around the active window on Windows 10/11.
C# replacement for the AutoHotkey app ActiveWindowDashedBorder.

## What it does

Click-through, always-on-top strip windows draw a dashed, animated rectangle on the visible frame
edge of whichever window has focus. The border follows window switches, hides while a window is
moved or resized, and returns once it is still. Minimised and full-screen windows are skipped.

## Usage

Run `ActiveAnts.exe`. No installer; copy it anywhere. A tray icon appears:

| Option | Action |
|--------|--------|
| Config... | Open the settings window (also on double-click) |
| Reload | Restart the app |
| Exit | Stop the app |

## Configuration

Settings live in an INI next to the exe, named after it: `ActiveAnts.exe` uses `ActiveAnts.ini`.
The file is created on the first Save in Config. Values are pixels at 100% scaling; on other
monitors they scale with that monitor's DPI.

| Section | Key | Default | Minimum | Description |
|---|---|---|---|---|
| Style | `borderColor` | `0xFFFFFF00` | | ARGB hex |
| Style | `borderWidth` | `4` | 1 | Stroke width |
| Style | `dashLen` | `8` | 1 | Dash length |
| Style | `gapLen` | `32` | 1 | Gap between dashes |
| Anim | `animStep` | `2` | 0 | Pixels the pattern advances per tick |
| Anim | `animMs` | `40` | 10 | Animation tick interval (ms) |
| Anim | `settleMs` | `120` | 0 | Delay after a window stops moving before redrawing (ms) |
| Anim | `syncForegroundMs` | `500` | 100 | Fallback interval to resync the active window (ms) |
| Anim | `clockwise` | `0` | | `1` walks the ants clockwise; `0` counterclockwise, as the AHK version |
| Debug | `enabled` | `0` | | `1` writes `ants-debug.log` next to the exe |

A missing, invalid or out-of-range value falls back to its default.

**Run at Windows startup** in Config writes `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`,
value `MarchingAnts` (no admin needed).

## Switching from the AHK version

ActiveAnts shares the AHK app's single-instance mutex and startup value, so only one of them runs.

1. Exit ActiveWindowDashedBorder.
2. Optionally copy `marching-ants.ini` next to `ActiveAnts.exe` as `ActiveAnts.ini`.
3. Start `ActiveAnts.exe`, open Config, tick **Run at Windows startup**. That replaces the AHK entry.

## Building

Requires the .NET 10 SDK.

```
dotnet test
dotnet publish src/ActiveAnts -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o publish
```

Output: `publish/ActiveAnts.exe`.

## Technical notes

- Per-Monitor V2 DPI aware via the manifest; all geometry in physical pixels. A window spanning
  monitors with different scaling gets its border split at the boundary, each part scaled to its
  monitor.
- Four thin layered strip windows instead of one window-sized overlay: per-frame pixel work scales
  with the perimeter, not the area.
- GDI+ draws into cached premultiplied-ARGB DIB sections; `UpdateLayeredWindow` pushes them.
  Geometry is recomputed on events, not per animation frame.
- `SetWinEventHook` for foreground and location changes; hooks are re-registered after
  sleep/hibernate resume. Display changes refresh the geometry without a restart.
- A watchdog every 4 seconds recovers from frozen timers, dead hooks or lost overlay windows.

## License

MIT; see [LICENSE](LICENSE).

## Author

Arco Witter
