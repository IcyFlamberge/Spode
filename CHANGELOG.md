# Changelog

All notable changes to Spode are recorded here.

## [1.0.0] - 2026-11-01

### Added

- Manual Spore executable selection; no registry probing or install discovery.
- Game build detection (Disc / Origin / EA / Steam / GOG) with manual override.
- `.package` installation into `DataEP1` or `Data`.
- `.sporemod` installation with `ModInfo.xml` parsing, components,
  prerequisites, compat files and removals.
- Central `Mods` library with generated `ModInfo.xml` for package mods.
- Profiles with independent mod lists and per-profile ModAPI versions.
- Idempotent profile activation with shared-file protection.
- GitHub ModAPI release listing, download progress, SHA-256 cache verification
  and local `Downloaded` / `Installed` marks.
- Bundled official `ModAPI.DLLInjector.dll` and x86 Spore launch with injection.
- Automatic injector bootstrap from the official Launcher Kit release.
- Simple tabbed WPF UI with Mods / Profiles / ModAPI / Launch / Settings /
  About.
- MIT licence, third-party notices and security policy.
- Release script with optional code signing, checksums and packaging.
- GitHub Actions release workflow with build provenance.
- Developer documentation under `docs/dev/`.

### Notes

- The release is unsigned unless a code-signing certificate is configured.
- Spode is MIT-licensed; third-party components keep their own licenses.
