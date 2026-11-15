<!--
Release notes template. Copy to notes-<version>.md, fill in, paste into GitHub's
"Describe this release". Replace every {PLACEHOLDER}; drop sections that do not apply.
- {VERSION}  new version, for example 1.0.2
- {PREVIOUS} previous published version, for example 1.0.1
- {SHA256}   from ActiveAnts-{VERSION}-win-x64.zip.sha256
Describe only what changed since {PREVIOUS}; the full overview stays in the 1.0.0 release
and the README. Keep the fixed parts (overview link, install, unsigned note, verify, footer).
This comment is not shown by GitHub, but delete it anyway.
-->
{One-line summary, for example: Small update to {PREVIOUS}.}

For the full feature overview, see the [1.0.0 release](https://github.com/ArcoW2/ActiveAnts/releases/tag/v1.0.0) or the [README](https://github.com/ArcoW2/ActiveAnts#readme).

## Changes

- {Change, user's point of view: what is new or fixed, where to find it.}

{Compatibility line, for example: No changes to settings; `ActiveAnts.ini` from {PREVIOUS} works as-is.}

## Install / update

Download `ActiveAnts-{VERSION}-win-x64.zip` and extract `ActiveAnts.exe` over the old one (exit Active ants first via the tray menu), or into any folder you can write to. Self-contained: no .NET installation needed.

**Unsigned:** Windows SmartScreen may show "Windows protected your PC" on first start. Click *More info* > *Run anyway*, or tick *Unblock* in the file's Properties first.

## Verify the download

SHA-256 of `ActiveAnts-{VERSION}-win-x64.zip`:

```
{SHA256}
```

PowerShell: `Get-FileHash .\ActiveAnts-{VERSION}-win-x64.zip -Algorithm SHA256`

---

Copyright (c) 2026 Arco Witter. Released under the [MIT License](https://github.com/ArcoW2/ActiveAnts/blob/main/LICENSE).
Like Active ants? [Support me on Ko-fi](https://ko-fi.com/arcowitter).
