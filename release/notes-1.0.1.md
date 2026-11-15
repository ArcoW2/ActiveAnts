Small update to 1.0.0.

## Changes

- Config window: **About...** button, next to Apply and Save & Close.
- About window: **Support on Ko-fi** link (https://ko-fi.com/arcowitter).
- Repository: Sponsor button on GitHub, linking to Ko-fi.

No changes to the border, animation or settings; `ActiveAnts.ini` from 1.0.0 works as-is.

## Install / update

Download `ActiveAnts-1.0.1-win-x64.zip` and extract `ActiveAnts.exe` over the old one (exit Active ants first via the tray menu), or into any folder you can write to. Self-contained: no .NET installation needed.

**Unsigned:** Windows SmartScreen may show "Windows protected your PC" on first start. Click *More info* > *Run anyway*, or tick *Unblock* in the file's Properties first.

## Verify the download

SHA-256 of `ActiveAnts-1.0.1-win-x64.zip`:

```
B90E8B2F6AD158C6C8F09B4ADE9472792B7E286D29BDB106A338DACFF5A9C1A1
```

PowerShell: `Get-FileHash .\ActiveAnts-1.0.1-win-x64.zip -Algorithm SHA256`
