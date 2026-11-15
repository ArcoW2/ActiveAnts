# Plan: ActiveAnts (AA), a C# replacement for ActiveWindowDashedBorder

## 1. What this is about

Enhancement: a new C# app, **ActiveAnts** (short name AA), that replaces the AutoHotkey v2 app in
`C:\Win32App\AhkScripts\ActiveWindowDashedBorder` (`ActiveWindowDashedBorder.ahk`). It draws an
animated "marching ants" dashed border around the active window. Delivery is a single exe with no
installer; settings live in an `.ini` next to the exe, like the AHK version. Functionally a full copy,
with the AHK defects found during analysis repaired rather than ported.

## 2. The solution, in brief

A WinForms tray app on .NET 10, published as a self-contained single-file exe. Wherever there is a
design choice, the option with the least CPU/GPU cost wins. The border is four thin click-through
layered strip windows, painted with GDI+ into cached premultiplied-ARGB DIBs and pushed with
`UpdateLayeredWindow`; geometry is recomputed on events, not per animation frame. The process is
Per-Monitor V2 DPI aware via its manifest, which fixes the mixed-DPI defect of the AHK version.
Settings are read and written through the Win32 private-profile API in `<exe name>.ini`. The app
shares the AHK app's mutex and startup registry value, so it takes the AHK app's place.

## 3. Details

### 3.1 Behaviour inventory (source of truth: the .ahk)

Everything below must exist in ActiveAnts. "Same" means same behaviour as the AHK script;
deviations are explained in the referenced section.

| Area | AHK behaviour | C# mapping |
|---|---|---|
| DPI | `SetProcessDpiAwarenessContext(-4)` intended Per-Monitor V2; in practice fails, process stays system aware (see 3.3) | `app.manifest` `<dpiAwareness>PerMonitorV2</dpiAwareness>`, physical pixels throughout |
| Single instance | `CreateMutex("Global\MarchingAntsOverlay")`, `WaitForSingleObject` 3000 ms, exit on timeout | Same name (shared with AHK, see 3.6); `System.Threading.Mutex`, `WaitOne(3000)`; treat `AbandonedMutexException` as acquired |
| Exit cleanup | Unhook both hooks, free pen + bitmap cache, GDI+ shutdown, close mutex | `ApplicationContext` exit handler; `Dispose` on renderer; release mutex |
| Tray | Menu: `Config...` (default, double-click), `Reload`, separator, `Exit`; icon = ant.ico | `NotifyIcon` + `ContextMenuStrip`; tooltip `Active ants`; icon embedded as `ApplicationIcon` and resource |
| Overlay | AlwaysOnTop, no caption, ToolWindow, `WS_EX_LAYERED`, `WS_EX_TRANSPARENT`, show no-activate | Four strip windows (see 3.4), each a `NativeWindow` subclass created with `WS_POPUP` and `WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE`; shown with `SW_SHOWNOACTIVATE` |
| Hooks | `EVENT_SYSTEM_FOREGROUND` and `EVENT_OBJECT_LOCATIONCHANGE`, `WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS` | P/Invoke `SetWinEventHook`; delegates held in fields so the GC never collects them |
| Foreground | `idObject != 0` ignored; re-query `GetForegroundWindow`, fallback to hook hwnd; clear drag; cancel settle; redraw after 30 ms | Same, one-shot timer 30 ms |
| Move/resize | Tracked window only; first event hides border and sets dragging; settle one-shot reset to `settleMs` on every event | Same |
| Settle | dragging=false, redraw | Same |
| Animate | Every `animMs`; heartbeat for watchdog; skip while dragging; `dashOffset = (dashOffset + animStep) mod (dashLen + gapLen)`; redraw | Frames paced on the compositor clock (first refresh at or after `animMs`), not a timer, so frames are evenly spaced on any refresh rate; offset from the frame timestamps (`animStep` per `animMs`), so steps are even; frozen while hidden or dragging. Each strip draws only its part of the path. Direction option `clockwise` (AHK walks counterclockwise) |
| Hide | overlayVisible=false, stop Animate timer, hide overlay | Same |
| Redraw | Hide if no window / gone / own overlay / minimised / full-screen; visible rect via `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` with `GetWindowRect` fallback; padding `ceil(w/2)+1`; draw; update layered; 10 consecutive errors -> restart | Same checks and outcome; full geometry on events only, Animate tick checks `IsWindow` only (see 3.4) |
| Full-screen test | Window rect vs `MonitorFromWindow(MONITOR_DEFAULTTONEAREST)` monitor rect, tolerance 4 px | Same, as a pure function for testing |
| Draw cache | DIB rebuilt on size change; pen rebuilt on `color|width|dash|gap` change; per frame: clear, set dash offset, draw rectangle | `Bitmap(w, h, stride, Format32bppPArgb, pBits)` over one DIB section per strip; `Pen` with `DashPattern` = len/width and `DashOffset` = offset/width; `SmoothingMode.AntiAlias`; pen key also includes the DPI scale (see 3.3) |
| UpdateLayered | `UpdateLayeredWindow` with `ULW_ALPHA`, `AC_SRC_OVER`, `AC_SRC_ALPHA`, const alpha 255; show; arm heartbeat; restart Animate | Same, per strip; position and size passed on every call (with `NULL` for them Windows did not show the new content: measured, only full redraws reached the screen) |
| Layout change | `WM_DISPLAYCHANGE`, `WM_DPICHANGED`, `WM_DEVICECHANGE` -> restart process after 1500 ms debounce | No restart. `WM_DISPLAYCHANGE` -> debounced full redraw; `WM_DPICHANGED` swallowed; `WM_DEVICECHANGE` ignored (see 3.5) |
| Power | `WM_POWERBROADCAST` with `PBT_APMRESUMESUSPEND` (7) or `PBT_APMRESUMEAUTOMATIC` (18) -> rehook after 2000 ms | Same |
| Rehook | Unhook, re-register; on failure restart; reset drag, settle, Animate, SyncForeground; redraw | Same |
| SyncForeground | Every `syncForegroundMs`: if foreground differs from current, switch and redraw | Same, plus refreshes geometry of the current window (see 3.4) |
| Watchdog | Every 4000 ms: overlay gone -> restart; hook handle zero -> rehook in 100 ms; Animate silent > `animMs*10` while visible -> restart timers | Same; "overlay gone" checks all four strips |
| Restart | Release mutex, start own exe, exit | `ReleaseMutex`, `Process.Start(Environment.ProcessPath)`, `Application.Exit` |
| Config window | Groups Style (color ARGB hex, width, dash, gap), Animation (step, interval, settle, sync), General (debug logging, run at startup); Apply; Save & Close; single instance; topmost; invalid number -> error box | WinForms `Form`, title `Active ants - Config`, same layout and controls; range validation (see 3.5) |
| Startup | `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `MarchingAnts`, quoted exe path; only write when state differs | Same key and value name (shared with AHK, see 3.6); `Microsoft.Win32.Registry`; state check compares the path (see 3.5) |
| Logging | `ants-debug.log` next to exe, `HH:mm:ss | msg`, only when `[Debug] enabled=1` | Same |

Not ported: `DoRecalibrate` is dead code in the AHK script (never referenced).

### 3.2 Settings file

- Name: the exe's own file name with `.ini`: `Path.ChangeExtension(Environment.ProcessPath, ".ini")`,
  so `ActiveAnts.exe` uses `ActiveAnts.ini`. A renamed exe uses its own INI.
- Location: directory of `Environment.ProcessPath` (not `AppContext.BaseDirectory`, which points to a
  temp extraction folder for some single-file setups).
- Format: same sections and keys as `marching-ants.ini`: `[Style] borderColor borderWidth dashLen
  gapLen`, `[Anim] animStep animMs settleMs syncForegroundMs clockwise` (`clockwise` is new), `[Debug] enabled`. Color stored as
  `0x%08X` hex string.
- Defaults (missing file or key) are the values of the current `marching-ants.ini`:

  | Key | Default |
  |---|---|
  | `borderColor` | `0xFFFFFF00` |
  | `borderWidth` | `4` |
  | `dashLen` | `8` |
  | `gapLen` | `32` |
  | `animStep` | `2` |
  | `animMs` | `40` |
  | `settleMs` | `120` |
  | `syncForegroundMs` | `500` |
  | `clockwise` (Anim) | `0` |
  | `enabled` (Debug) | `0` |

- Invalid or out-of-range value in the INI: that key falls back to its default, logged when debug is
  on (ranges in 3.5).
- Read/write through `GetPrivateProfileStringW` / `WritePrivateProfileStringW`. The existing AHK file
  is UTF-16 LE with BOM; the Win32 API handles that, so copying `marching-ants.ini` to
  `ActiveAnts.ini` carries the settings over.
- Not writable location (for example under Program Files): out of scope; Save shows an error box
  instead of failing silently.

### 3.3 Mixed-DPI defect in the AHK version, and the fix

Symptom: with monitors at different scaling, a window spanning two monitors gets a border sized for
one monitor's DPI, stretched onto the other.

Cause: the compiled AHK exe's manifest declares `<dpiAware>true</dpiAware>` (system DPI aware) and no
`<dpiAwareness>` element (checked by searching the exe's bytes for its manifest). Once a manifest sets
awareness, `SetProcessDpiAwarenessContext` fails with `ERROR_ACCESS_DENIED`; the script ignores the
return value, so the process stays system aware, not Per-Monitor V2. Consequences:
- On a monitor whose DPI differs from the system DPI, Windows virtualizes coordinates for the overlay
  and DWM bitmap-stretches it.
- `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` is not virtualized and returns physical pixels,
  so the rect being drawn and the coordinate space of the overlay do not match.
- The overlay is one surface, scaled by one factor, so a window spanning two monitors is outlined
  correctly on at most one of them.

Fix in ActiveAnts:
- Per-Monitor V2 declared in `app.manifest` (`<dpiAwareness>PerMonitorV2</dpiAwareness>`), no runtime
  call. At startup, check `GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext())` is
  per-monitor and log it when debug is on.
- The strips are raw windows (`NativeWindow` subclass with `CreateWindowEx`), not WinForms `Form`s.
  A per-monitor-aware `Form` rescales and moves itself on `WM_DPICHANGED`, which would break them.
- `WM_DPICHANGED` on a strip is swallowed: no resize, no restart.
- All geometry stays in physical pixels end to end.

Priority 1, position: the border follows the true window frame. The stroke is centred on the
visible frame edge (`DWMWA_EXTENDED_FRAME_BOUNDS`, physical pixels) on every monitor, including both
parts of a window spanning monitors. A spanning window is rendered at one DPI by Windows, so its frame
is one consistent physical rectangle; the border uses that rectangle as-is, never a per-monitor
recalculation of it.

Priority 2, stroke size, per monitor: the INI values (width, dash, gap, step) are pixels at 100%
scaling (96 DPI). Each part of the border is scaled by `dpi / 96` of the monitor it is on
(`GetDpiForMonitor`, `MDT_EFFECTIVE_DPI`). For a spanning window the strips are split at the monitor
boundary, each piece with its own scale and padding; a thicker piece grows equally inward and outward
around the same frame edge, so position does not shift. Accepted side effect: dash and gap lengths
change at the boundary, so the pattern jumps there.

### 3.4 Resource usage: least CPU/GPU wins

Rule for this project: when there is a choice, take the option with the least CPU/GPU cost, as long
as the visible behaviour stays the same. Where the plan departs from the AHK internals for this
reason, it is listed here.

Where the AHK version spends its cycles, per animation frame (default every 40 ms):
- Clears the whole window-sized ARGB bitmap, although only the border pixels are ever non-transparent.
- `UpdateLayeredWindow` copies that whole bitmap to DWM, which uploads and composites a surface the
  size of the window. For a maximised window that is a full-screen surface, every frame, mostly
  transparent.
- Recomputes geometry each frame: window exists, minimised, full-screen test (monitor lookup), DWM
  frame bounds.

Choices in ActiveAnts:
1. **Four strip windows instead of one window-sized overlay.** Top and bottom strips span the full
   width including the corners; left and right strips fill the height between them. Each strip is
   `borderWidth + 2*padding` thick (scaled). Pixels cleared, copied and composited per frame scale
   with the perimeter instead of the area. Dash continuity around the corners: each strip draws the
   same full rectangle with a translate transform, and GDI+ clips to the strip bitmap, so the pattern
   is one continuous path. On a window spanning monitors the strips crossing a boundary are split
   per monitor (3.3); extra windows only while spanning, pixel work about the same.
2. **Geometry on events, not per frame.** Full geometry (visible rect, minimised, full-screen, monitor
   DPI) is computed on foreground change, settle, rehook, config apply, display change and each
   SyncForeground tick. The Animate tick only checks `IsWindow` on the tracked HWND, redraws the
   strips at the known position (position and size are still passed to `UpdateLayeredWindow`, see 3.1).
3. **No restart on display changes** (see 3.5): a redraw is cheaper than a process restart.
4. **No work while hidden or dragging** (already AHK behaviour): Animate timer stopped when hidden,
   tick skips while dragging.
5. **Caches** (already AHK behaviour): strip DIBs rebuilt only on size change, pen only on style or
   DPI change.

Risk of choice 1: four separate `UpdateLayeredWindow` calls per frame may land in different DWM frames,
which could show as a flicker at the corners. If that is visible, fall back to the single overlay;
decided by testing (3.9), not in advance.

Measurement: CPU time of the process over a fixed period (`TotalProcessorTime` delta) and GPU usage of
the process and `dwm.exe` (Task Manager / performance counters `GPU Engine`), for the AHK exe, a
single-overlay build and the strip build, with a maximised window and the default settings.
Results: process CPU and GPU [not yet measured]. Draw-only CPU benchmark (GDI+ clear + dashed
rectangle, 3840x2160 frame, defaults, 500 frames, scratch console app): single overlay 3.515
ms/frame and 31.8 MB/frame handed to `UpdateLayeredWindow`; four strips 1.245 ms/frame and 0.27
MB/frame.

### 3.5 AHK defects repaired

| AHK defect | Repair |
|---|---|
| DPI awareness silently not applied | Manifest, see 3.3 |
| Default color in code (`0xFFFF0000`) differs from README and shipped INI (`0xFFFFFF00`) | One set of defaults, from the current INI (3.2) |
| `OnLocation` logs its errors as "Redraw error" | Logs as "OnLocation error" |
| `IsStartupEnabled` is true for any value under `MarchingAnts`, even one pointing at another exe | Enabled only when the value equals this exe's quoted path (case-insensitive). A value pointing at the AHK exe shows unchecked; checking it replaces the AHK entry |
| `SetStartup` failure only logged | Error box in the config window, plus log |
| No range checks: `borderWidth=0` divides by zero in the dash array; zero or negative values accepted | Lower bounds enforced in the config window (error box naming the field) and on INI load (default + log): `borderWidth >= 1`, `dashLen >= 1`, `gapLen >= 1` (GDI+ dash entries must be positive), `animStep >= 0`, `animMs >= 10`, `settleMs >= 0`, `syncForegroundMs >= 100`; `borderColor` must parse as 32-bit hex |
| Config error box says only "One of the values isn't a valid number" | Names the field and its allowed range |
| Process restart on `WM_DISPLAYCHANGE`, `WM_DPICHANGED` and any `WM_DEVICECHANGE` (a USB stick too). The restart was a workaround for the fixed system DPI of a system-aware process | Per-Monitor V2 removes the need: `WM_DISPLAYCHANGE` -> debounced (1500 ms) full geometry + DPI refresh; `WM_DPICHANGED` swallowed; `WM_DEVICECHANGE` ignored. Monitor add/remove also sends `WM_DISPLAYCHANGE` |
| Save to a non-writable INI fails silently | Error box |

Restart remains only where AHK uses it as recovery: rehook failure, 10 consecutive redraw errors,
overlay window gone, and the tray `Reload` item.

### 3.6 Replacing the AHK app

- Shared identity: mutex `Global\MarchingAntsOverlay` and Run value `MarchingAnts`. ActiveAnts and the
  AHK exe exclude each other; whichever starts first wins.
- Switch-over: stop the AHK exe, copy `ActiveAnts.exe` anywhere, optionally copy `marching-ants.ini`
  next to it as `ActiveAnts.ini`, start it, tick "Run at Windows startup" in Config. That overwrites
  the AHK startup entry.

### 3.7 Technical points that differ from AHK

- **Message window.** AHK `OnMessage` sees messages for all script windows. In C# the broadcasts
  (`WM_DISPLAYCHANGE`, `WM_POWERBROADCAST`) only reach top-level windows, not message-only
  (`HWND_MESSAGE`) windows. Handle them in the top strip's `WndProc` (it is a top-level window).
- **Timers.** AHK negative period = one-shot. Map each AHK timer to its own
  `System.Windows.Forms.Timer`; one-shots stop themselves in `Tick`. All callbacks stay on the UI
  thread, matching AHK's single thread, so no locking.
- **Hook callbacks.** `WINEVENT_OUTOFCONTEXT` callbacks are delivered through the UI thread message
  loop; WinForms provides it.
- **Mutex ownership.** .NET mutexes are thread-affine; acquire and release on the UI thread.
- **GDI+.** `System.Drawing` starts GDI+ itself; no explicit `GdiplusStartup`.
- **ASCII.** UI strings use plain ASCII: `Config...`, `Active ants - Config`, `Save && Close` (the
  `&&` renders a literal `&` in WinForms, same as AHK).

### 3.8 Packaging (no install)

- `OutputType=WinExe`, `TargetFramework=net10.0-windows`, `UseWindowsForms=true`.
- `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true`.
- Output: one `ActiveAnts.exe`. User copies it anywhere; `ActiveAnts.ini` is created next to it on
  first Save. Exe size: 52,088,326 bytes (`ls -la publish`, .NET SDK 10.0.403).
- Trimming off (WinForms does not support it).

### 3.9 Project layout

```
ActiveAnts.slnx
src/ActiveAnts/
  ActiveAnts.csproj
  app.manifest           DPI awareness, asInvoker
  ant.ico                copied from the AHK folder
  Program.cs             mutex, run TrayContext
  TrayContext.cs         ApplicationContext: tray, timers, watchdog, restart, exit cleanup
  OverlayWindow.cs       layered click-through NativeWindow (one strip); the top strip's WndProc
                         handles display/power messages
  BorderRenderer.cs      strip layout, per-strip DIB + Bitmap + Graphics, shared Pen, UpdateLayeredWindow
  WindowTracker.cs       WinEvent hooks, foreground/location logic, rehook
  WindowGeometry.cs      visible rect, full-screen test, padding, DPI scale (pure where possible)
  Settings.cs            load/save via private-profile API, defaults, range checks
  ConfigForm.cs          config window
  StartupRegistration.cs Run key
  Log.cs                 debug log
  Native.cs              P/Invoke declarations (LibraryImport)
tests/ActiveAnts.Tests/
  ActiveAnts.Tests.csproj  xUnit
README.md  LICENSE  .gitignore
```

The local folder is still named `BorderAnts`; renaming it is up to the user.

### 3.10 Implementation order

Precondition: the user has created the GitHub repo; work happens in a clone of it.

1. Solution, csproj, manifest, icon, `.gitignore`; empty tray app with menu and Exit. Publish
   single-file, confirm it runs from an arbitrary folder and reports Per-Monitor V2.
2. `Settings` + tests (defaults, ranges, round-trip, reading the existing UTF-16 AHK INI).
3. Strip overlays + renderer: static dashed border on a fixed rect, then animated. Check corners for
   flicker; measure against a single-overlay variant (3.4) and keep the cheaper one that looks right.
4. `WindowTracker`: foreground + location hooks, settle, animate, sync.
5. Geometry: visible rect, minimised, full-screen skip, DPI scaling.
6. Config window (with validation) + startup registration.
7. Resilience: display/power messages, rehook, watchdog, error counter, restart, mutex handoff.
8. Debug log.
9. README for ActiveAnts.

### 3.11 Tests

Automated (xUnit):
- Settings: defaults when file missing; defaults per missing key; each range bound (at and one below
  the minimum); invalid hex color; round-trip of every key; reading a copy of the real
  `marching-ants.ini` (test data); INI name derived from exe name.
- Geometry: padding `ceil(width/2)+1` and overlay rect; full-screen test with tolerance edges
  (exactly 4 px in, 5 px in); dash offset wrap; DPI scale at 96, 120, 144, 192.
- Strip layout: four strips cover exactly the padded rectangle, no overlap and no gap, for odd and
  even border widths and for tiny windows (height smaller than two strips).
- Spanning layout: a frame crossing a vertical, horizontal and both monitor boundaries is split into
  pieces that tile the border without gap or overlap; each piece's stroke centre lies on the frame
  edge, whatever its scale.
- Pen key: changes in color, width, dash, gap or DPI scale force rebuild; other changes do not.
- Startup: state check true only for this exe's path, case-insensitive; false for the AHK exe path.

Manual, with the AHK exe stopped:
- Window switch by click and Alt-Tab; border lands on the visible edge on all sides.
- Drag and resize: border hides while moving, returns after settle.
- Minimise and full-screen video: border hidden.
- Two monitors with different scaling (for example 100% and 150%), primary on either one: border
  on the visible frame edge for a window fully on each monitor and for a window spanning both, on
  both parts; each part scaled to its monitor. Drag a window across the boundary: no restart, no
  stretch.
- Monitor add/remove, resolution change, dock/undock: border returns at the right place, no restart.
  USB device plugged in: nothing happens.
- Sleep and hibernate resume: hooks still work.
- Config: Apply takes effect live; Save & Close writes `ActiveAnts.ini`; each invalid field shows its
  own error.
- Run at startup: with the AHK entry present the box is unchecked; checking replaces it; app starts
  after login.
- AHK exe running: ActiveAnts exits quietly, and the other way round. Reload: new instance takes over
  without the 3 s wait.
- Exe copied to a fresh folder with no INI: runs with defaults. Renamed exe: uses its own INI.
- Resource use: the measurement in 3.4, AHK exe vs ActiveAnts, maximised window, defaults. Corners
  show no flicker while animating.

## 4. Open questions

None.
