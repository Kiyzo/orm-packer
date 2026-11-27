# NOTICE

ORM Packer — Copyright (c) 2026 Kiyzo. Licensed under the MIT License (see `LICENSE`).

## Bundled third-party components

`OrmPacker.exe` embeds a native ImageMagick backend solely to decode EXR maps (DWAA and other OpenEXR compressions). All other formats use built-in decoders or GDI+.

1. **Magick.NET-Q16-AnyCPU 15.1.0** — managed .NET wrapper.
   Copyright (c) Dirk Lemstra and contributors. Licensed under the Apache License, Version 2.0.
   Source: https://github.com/dlemstra/Magick.NET

2. **ImageMagick native backend** — bundled by Magick.NET.
   Copyright (c) 1999 ImageMagick Studio LLC. Licensed under the ImageMagick License.
   Source: https://imagemagick.org/

The browser version bundles the following npm packages (resolved from `web/package-lock.json`
and inlined into `wwwroot/js/exr-bundle.js` / `exrs_raw_wasm_bindgen_bg.wasm` by `npm run build`):

3. **exrs 1.0.4** — Rust/WASM OpenEXR decoder with DWAA/DWAB support.
   Copyright (c) Johannes Vollmer and contributors. Licensed under the BSD 3-Clause License.
   Source: https://github.com/johannesvollmer/exrs

4. **exrs-raw-wasm-bindgen 0.1.0** — generated wasm-bindgen glue and the shipped `.wasm` binary,
   bundled inside `exrs`. Copyright (c) Johannes Vollmer and contributors.
   Licensed under the BSD 3-Clause License. Source: https://github.com/johannesvollmer/exrs

5. **wasm-bindgen runtime** — the generated JS glue shipped inside `exrs-raw-wasm-bindgen`
   (`exr-bundle.js`). Copyright (c) 2014 Alex Crichton and the wasm-bindgen developers.
   Dual-licensed under the MIT License **or** the Apache License, Version 2.0 (we use MIT).
   Source: https://github.com/rustwasm/wasm-bindgen

6. **shallow-equals 1.0.0** — small deep-equality helper inlined by the bundler.
   Copyright (c) 2014 Hugh Kennedy. Licensed under the MIT License.
   Source: https://github.com/JedWatson/shallow-equals

Build-time only, **not shipped in the browser bundle**: **esbuild 0.28.2** (MIT,
Copyright (c) 2020 Evan Wallace) with its platform-specific `@esbuild/*` binaries (MIT),
and **tslib 2.9.1** (0BSD, Copyright (c) Microsoft Corporation), a declared `exrs`
dependency that the current bundle does not import.

Full license texts for all bundled components are reproduced in `THIRD_PARTY_LICENSES.md`.

ImageMagick Studio LLC and the Magick.NET authors do not endorse or sponsor ORM Packer. The ImageMagick name is used only to attribute the bundled decoder.

## Compatibility

The ImageMagick License and Apache License 2.0 are permissive and compatible with redistribution of ORM Packer under MIT.
