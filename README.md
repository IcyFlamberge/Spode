# Spode

Spode is a Spore mod manager and launcher. It supports traditional `.package`
mods, `.sporemod` containers, profiles, ModAPI versions and Spore launch with
the official ModAPI injector.

## Requirements

- Windows 10/11 (x86 build);
- Spore + Galactic Adventures;
- no separate .NET installation is required.

## Quick start

1. Run `Spode.exe`.
2. Open **Settings** and select `SporeApp.exe` from `...\SporebinEP1`.
3. Open **ModAPI**, choose a version, click **Download**, then **Apply to launcher**.
4. Open **Mods** to install `.package` or `.sporemod` files.
5. Open **Profiles**, create a profile, add mods, click **Activate profile**.
6. Open **Launch** and click **Launch Spore**.

## Files

- `Spode.exe` — the application;
- `dll\ModAPI.DLLInjector.dll` — official 32-bit ModAPI injector;
- `licenses\` — licence and third-party notices;
- `SHA256SUMS.txt` — SHA-256 checksums of the release files.

## Notes

- This build is unsigned unless a code-signing certificate was used. Windows
  SmartScreen may show a warning.
- The ModAPI core DLLs are downloaded from the official
  `Spore-Community/Spore-ModAPI` GitHub releases.
- Spode is proprietary software. See `licenses/LICENSE`.
- Mods are not created by Spode and remain governed by their authors' licences.
