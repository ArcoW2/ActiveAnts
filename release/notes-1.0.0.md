First release of **Active ants**: an animated "marching ants" border around the active window, for Windows 10/11 (x64). C# replacement for the AutoHotkey app ActiveWindowDashedBorder.

## Install

1. Download `ActiveAnts-1.0.0-win-x64.zip` and extract `ActiveAnts.exe` to any folder you can write to (not under Program Files: settings are saved next to the exe).
2. Run it. A tray icon appears; double-click it for Config.

No installer and no .NET installation needed: the exe is self-contained.

**Unsigned:** Windows SmartScreen may show "Windows protected your PC" on first start. Click *More info* > *Run anyway*, or tick *Unblock* in the file's Properties first.

## Highlights

- Border on the visible window edge, Per-Monitor V2 DPI aware; split per monitor when a window spans screens with different scaling.
- Animation paced on the display refresh: even steps, no timer jitter.
- Width per edge (0-10, 0 hides an edge), dash and gap length, colour, speed, direction (clockwise or counterclockwise).
- Settings in `ActiveAnts.ini` next to the exe; an existing AHK `marching-ants.ini` can be copied as `ActiveAnts.ini`.
- About window (tray menu and Config): version, author, project link and the MIT license.
- Shares the AHK version's single-instance lock and startup entry, so it replaces it cleanly. See the README for switching over.

## Verify the download

SHA-256 of `ActiveAnts-1.0.0-win-x64.zip`:

```
57879BD2091E333B30E4625674184F43225065BFA6E99C3691B98B4B8EE57E88
```

PowerShell: `Get-FileHash .\ActiveAnts-1.0.0-win-x64.zip -Algorithm SHA256`
