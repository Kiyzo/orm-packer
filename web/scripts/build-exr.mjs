import { build } from 'esbuild';
import { copyFileSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const outDir = join(process.cwd(), 'OrmPacker.Web', 'wwwroot', 'js');

await build({
  entryPoints: [join(process.cwd(), 'src', 'exr-entry.js')],
  bundle: true,
  format: 'iife',
  platform: 'browser',
  outfile: join(outDir, 'exr-bundle.js'),
  logLevel: 'warning'
});

// wasm-bindgen resolves the wasm URL against import.meta.url, which is empty
// in an IIFE bundle. Resolve it against the page <base href> instead.
const bundlePath = join(outDir, 'exr-bundle.js');
const bundle = readFileSync(bundlePath, 'utf8');
const patched = bundle.replace(
  'new URL("exrs_raw_wasm_bindgen_bg.wasm", import_meta.url)',
  'new URL("js/exrs_raw_wasm_bindgen_bg.wasm", document.baseURI)'
);
// Fail loudly if a dependency update changes the generated wasm URL pattern.
if (patched === bundle) throw new Error('Could not patch the wasm URL in exrs bundle');
writeFileSync(bundlePath, patched);

const wasmName = 'exrs_raw_wasm_bindgen_bg.wasm';
let bindgenDir;
try {
  bindgenDir = dirname(require.resolve('exrs-raw-wasm-bindgen/package.json'));
} catch {
  const exrsDir = dirname(require.resolve('exrs/package.json'));
  bindgenDir = join(exrsDir, 'node_modules', 'exrs-raw-wasm-bindgen');
}
copyFileSync(join(bindgenDir, wasmName), join(outDir, wasmName));
console.log(`Bundled exrs decoder and ${wasmName} into ${outDir}`);
