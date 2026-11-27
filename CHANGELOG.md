# Changelog

## [1.0.0] - 2026-11-27

First public release. One shared core powers the Windows app, the browser app and the CLI.

- **Pack and Convert:** preset-based channel packing, automatic map detection by file name, drag & drop, per-channel invert and constant values, full-res channel unpacking, and conversion between presets with automatic Roughness ↔ Smoothness inversion.
- **Presets:** ORM, Unity HDRP Mask Map, Unity URP/Built-in and Custom; deterministic 8-bit output up to 8192 px with PNG/JPEG/TGA/BMP export and RGB/RGBA/R/G/B/A previews.
- **Inputs:** PNG/APNG, JPG, BMP, GIF, TIFF, ICO, TGA and EXR (including DWAA) on Windows; PNG, JPG, BMP, GIF, WebP, TGA and EXR in the browser.
- **Shells:** offline Windows UI with RU/EN languages and saved settings, Blazor WASM web app with a `localStorage` session, and `pack`, `convert`, `batch`, `unpack` CLI commands.
- **Release:** signed single-file exe, two archives with license files and `SHA256SUMS.txt`; third-party components are attributed in `NOTICE.md` and `THIRD_PARTY_LICENSES.md`.

## [Unreleased]
