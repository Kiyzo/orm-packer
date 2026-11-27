(function () {
    'use strict';

    // MAX_FILE_BYTES/MAX_PIXELS mirror Index.razor; MAX_OUTPUT_PIXELS is a JS-side export cap.
    // Validate here, before anything crosses into WASM.
    const MAX_FILE_BYTES = 24 * 1024 * 1024;
    const MAX_PIXELS = 32 * 1024 * 1024;
    const MAX_OUTPUT_PIXELS = 80 * 1024 * 1024;
    const SUPPORTED = /\.(png|jpe?g|webp|bmp|gif|tga|exr)$/i;

    const fail = (code) => { throw new Error(code); };
    const normChannel = (value) => {
        const n = Number(value);
        return Number.isInteger(n) && n >= 0 && n <= 3 ? n : -1;
    };

    const toBytes = (value) => {
        if (value instanceof Uint8Array) return value;
        if (Array.isArray(value)) return Uint8Array.from(value);
        if (typeof value !== 'string' || value.length === 0) fail('decode-invalid');
        const binary = atob(value);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
        return bytes;
    };

    const toBase64 = (bytes) => {
        let binary = '';
        const chunk = 0x8000;
        for (let i = 0; i < bytes.length; i += chunk) {
            binary += String.fromCharCode.apply(null, bytes.subarray(i, i + chunk));
        }
        return btoa(binary);
    };

    const canvasFor = (width, height) => {
        const canvas = document.createElement('canvas');
        canvas.width = width;
        canvas.height = height;
        return canvas;
    };

    const checkSize = (width, height, limit = MAX_PIXELS) => {
        if (!Number.isInteger(width) || !Number.isInteger(height) || width <= 0 || height <= 0 ||
            width * height > limit) fail('decode-too-large');
    };

    const bgraToImageData = (context, width, height, bgra) => {
        const image = context.createImageData(width, height);
        const target = image.data;
        for (let i = 0, j = 0; i < target.length; i += 4, j += 4) {
            target[i] = bgra[j + 2];
            target[i + 1] = bgra[j + 1];
            target[i + 2] = bgra[j];
            target[i + 3] = bgra[j + 3];
        }
        context.putImageData(image, 0, 0);
    };

    // Rejects mislabeled or truncated files before they reach a decoder.
    const signatureError = async (file) => {
        const name = (file.name || '').toLowerCase();
        let header;
        try {
            header = new Uint8Array(await file.slice(0, 12).arrayBuffer());
        } catch {
            return 'unreadable';
        }
        if (header.length < 4) return 'corrupt';
        const is = (...bytes) => bytes.every((b, i) => header[i] === b);
        if (name.endsWith('.png')) return is(0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a) ? '' : 'corrupt';
        if (name.endsWith('.jpg') || name.endsWith('.jpeg')) return is(0xff, 0xd8) ? '' : 'corrupt';
        if (name.endsWith('.bmp')) return is(0x42, 0x4d) ? '' : 'corrupt';
        if (name.endsWith('.gif')) return is(0x47, 0x49, 0x46, 0x38) ? '' : 'corrupt';
        if (name.endsWith('.webp')) return is(0x52, 0x49, 0x46, 0x46) ? '' : 'corrupt';
        if (name.endsWith('.exr')) return is(0x76, 0x2f, 0x31, 0x01) ? '' : 'corrupt';
        return ''; // TGA has no reliable signature; the decoder validates it.
    };

    const readFiles = async (fileList, channel) => {
        const result = [];
        for (const file of fileList || []) {
            let code = '';
            if (!file || file.size === 0) code = 'empty';
            else if (file.size > MAX_FILE_BYTES) code = 'too-large';
            else if (!SUPPORTED.test(file.name || '')) code = 'unsupported';
            else code = await signatureError(file);

            let data = '';
            if (!code) {
                try {
                    data = await new Promise((resolve, reject) => {
                        const reader = new FileReader();
                        reader.onload = () => resolve((reader.result || '').split(',')[1] || '');
                        reader.onerror = () => reject(reader.error);
                        reader.readAsDataURL(file);
                    });
                    if (!data) code = 'empty';
                } catch {
                    code = 'unreadable';
                }
            }
            result.push({ name: (file && file.name) || 'file', data, channel: normChannel(channel), error: code });
        }
        return result;
    };

    const download = (blob, name) => {
        const url = URL.createObjectURL(blob);
        const anchor = document.createElement('a');
        anchor.href = url;
        anchor.download = name;
        document.body.appendChild(anchor);
        anchor.click();
        anchor.remove();
        // Let the click start the download before revoking the blob URL.
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    };

    const bmpBytes = (width, height, bgra) => {
        const rowSize = Math.floor((width * 3 + 3) / 4) * 4;
        const bytes = new Uint8Array(54 + rowSize * height);
        const view = new DataView(bytes.buffer);
        bytes[0] = 0x42; bytes[1] = 0x4d;
        view.setUint32(2, bytes.length, true);
        view.setUint32(10, 54, true);
        view.setUint32(14, 40, true);
        view.setInt32(18, width, true);
        view.setInt32(22, height, true);
        view.setUint16(26, 1, true);
        view.setUint16(28, 24, true);
        view.setUint32(34, rowSize * height, true);
        for (let y = 0; y < height; y++) {
            for (let x = 0; x < width; x++) {
                const src = ((height - 1 - y) * width + x) * 4;
                const dst = 54 + y * rowSize + x * 3;
                bytes[dst] = bgra[src];
                bytes[dst + 1] = bgra[src + 1];
                bytes[dst + 2] = bgra[src + 2];
            }
        }
        return bytes;
    };

    const tgaBytes = (width, height, bgra) => {
        const bytes = new Uint8Array(18 + width * height * 4);
        bytes[2] = 2;
        bytes[12] = width & 0xff; bytes[13] = (width >> 8) & 0xff;
        bytes[14] = height & 0xff; bytes[15] = (height >> 8) & 0xff;
        bytes[16] = 32;
        bytes[17] = 0x28;
        bytes.set(bgra, 18);
        return bytes;
    };

    let exrReady = null;
    window.ormPacked = null;
    window.ormFullPacked = null;
    window.ormSource = null;
    let previewFrame = 0;

    window.ormInterop = {
        install: async function (dotnetRef) {
            const call = (name, ...args) =>
                dotnetRef.invokeMethodAsync(name, ...args).catch(err => console.error('orm-packer interop:', err));

            const input = document.getElementById('orm-file-input');
            input.addEventListener('change', async () => {
                const channel = window.ormPickChannel ?? -1;
                window.ormPickChannel = -1;
                try {
                    const files = await readFiles(input.files, channel);
                    await call('OnFiles', files);
                } catch (err) {
                    console.error('orm-packer file intake:', err);
                } finally {
                    input.value = '';
                }
            });

            document.addEventListener('dragover', (e) => {
                e.preventDefault();
                document.body.classList.add('drag-over');
            });
            document.addEventListener('dragleave', (e) => {
                if (e.relatedTarget === null) document.body.classList.remove('drag-over');
            });
            document.addEventListener('drop', async (e) => {
                e.preventDefault();
                document.body.classList.remove('drag-over');
                const target = e.target instanceof Element ? e.target.closest('[data-channel]') : null;
                const channel = target ? normChannel(target.getAttribute('data-channel')) : -1;
                try {
                    const files = await readFiles(e.dataTransfer.files, channel);
                    await call('OnFiles', files);
                } catch (err) {
                    console.error('orm-packer file intake:', err);
                }
            });

            document.addEventListener('keydown', (e) => {
                const tag = (e.target.tagName || '').toLowerCase();
                if (tag === 'input' || tag === 'select' || tag === 'textarea') return;
                const channel = e.key >= '1' && e.key <= '4' ? Number(e.key) - 1 : -1;
                if (channel >= 0 || (e.ctrlKey && (e.key === 'k' || e.key === 's'))) {
                    e.preventDefault();
                    call('OnKey', channel, e.ctrlKey ? e.key.toLowerCase() : '');
                }
            });
        },

        pickFiles: function (channel) {
            window.ormPickChannel = normChannel(channel);
            document.getElementById('orm-file-input').click();
        },

        initExr: async function () {
            if (!exrReady) {
                if (!window.OrmExr) fail('decode-failed');
                exrReady = window.OrmExr.init().catch(() => { exrReady = null; fail('decode-failed'); });
            }
            await exrReady;
        },

        decodeExr: async function (base64) {
            await window.ormInterop.initExr();
            const bytes = toBytes(base64);
            let image;
            try {
                image = window.OrmExr.decodeExr(bytes);
            } catch {
                fail('decode-failed');
            }
            const width = image.width | 0;
            const height = image.height | 0;
            checkSize(width, height);
            const layer = image.layers.find(l => l.containsChannelNames(['R', 'G', 'B'])) || image.layers[0];
            if (!layer) fail('decode-invalid');
            const count = width * height;
            const rgb = layer.getInterleavedPixels(['R', 'G', 'B']);
            const alpha = layer.getInterleavedPixels(['A']);
            if (rgb && rgb.length < count * 3) fail('decode-invalid');
            if (alpha && alpha.length < count) fail('decode-invalid');
            const bgra = new Uint8Array(count * 4);
            const clamp = v => Math.max(0, Math.min(255, Math.round(v * 255)));
            if (rgb) {
                for (let i = 0; i < count; i++) {
                    bgra[i * 4] = clamp(rgb[i * 3 + 2]);
                    bgra[i * 4 + 1] = clamp(rgb[i * 3 + 1]);
                    bgra[i * 4 + 2] = clamp(rgb[i * 3]);
                    bgra[i * 4 + 3] = alpha ? clamp(alpha[i]) : 255;
                }
            } else {
                const y = layer.getInterleavedPixels(['Y']) || layer.getAllInterleavedPixels();
                if (!y || y.length < count) fail('decode-invalid');
                for (let i = 0; i < count; i++) {
                    const value = clamp(y[i]);
                    bgra[i * 4] = value;
                    bgra[i * 4 + 1] = value;
                    bgra[i * 4 + 2] = value;
                    bgra[i * 4 + 3] = alpha ? clamp(alpha[i]) : 255;
                }
            }
            return { width, height, pixels: toBase64(bgra) };
        },

        decodeImage: async function (base64) {
            const bytes = toBytes(base64);
            let bitmap;
            try {
                bitmap = await createImageBitmap(new Blob([bytes]));
            } catch {
                fail('decode-failed');
            }
            try {
                const width = bitmap.width | 0;
                const height = bitmap.height | 0;
                checkSize(width, height);
                const canvas = canvasFor(width, height);
                const context = canvas.getContext('2d', { willReadFrequently: true });
                context.drawImage(bitmap, 0, 0);
                const data = context.getImageData(0, 0, width, height).data;
                const bgra = new Uint8Array(data.length);
                for (let i = 0; i < data.length; i += 4) {
                    bgra[i] = data[i + 2];
                    bgra[i + 1] = data[i + 1];
                    bgra[i + 2] = data[i];
                    bgra[i + 3] = data[i + 3];
                }
                return { width, height, pixels: toBase64(bgra) };
            } catch (e) {
                // Keep our own decode-* codes readable; wrap anything else as decode-failed.
                if (e && e.message && /^decode-/.test(e.message)) throw e;
                fail('decode-failed');
            } finally {
                bitmap.close();
            }
        },

        renderSlotThumb: function (width, height, base64, offset, invert) {
            try {
                const w = width | 0, h = height | 0;
                checkSize(w, h);
                const bytes = toBytes(base64);
                if (bytes.length < w * h * 4) return '';
                const size = 56;
                const canvas = canvasFor(size, size);
                const context = canvas.getContext('2d');
                const image = context.createImageData(size, size);
                const target = image.data;
                const component = Number(offset) >= 0 && Number(offset) <= 3 ? Number(offset) : -1;
                for (let y = 0; y < size; y++) {
                    const sy = Math.min(Math.floor(y * h / size), h - 1);
                    for (let x = 0; x < size; x++) {
                        const sx = Math.min(Math.floor(x * w / size), w - 1);
                        const i = (sy * w + sx) * 4;
                        let value;
                        if (component >= 0) {
                            value = bytes[i + component];
                        } else {
                            value = (bytes[i + 2] * 299 + bytes[i + 1] * 587 + bytes[i] * 114) / 1000;
                        }
                        if (invert) value = 255 - value;
                        const out = (y * size + x) * 4;
                        target[out] = value;
                        target[out + 1] = value;
                        target[out + 2] = value;
                        target[out + 3] = 255;
                    }
                }
                context.putImageData(image, 0, 0);
                return canvas.toDataURL('image/png');
            } catch {
                return '';
            }
        },

        renderSolidThumb: function (value) {
            const canvas = canvasFor(56, 56);
            const context = canvas.getContext('2d');
            const image = context.createImageData(56, 56);
            const v = Math.max(0, Math.min(255, Number(value) | 0));
            for (let i = 0; i < image.data.length; i += 4) {
                image.data[i] = v;
                image.data[i + 1] = v;
                image.data[i + 2] = v;
                image.data[i + 3] = 255;
            }
            context.putImageData(image, 0, 0);
            return canvas.toDataURL('image/png');
        },

        // All three setters stream bytes via DotNetStreamReference: a base64 string this large would bloat the JSON bridge.
        setPacked: async function (width, height, streamRef) {
            const w = width | 0, h = height | 0;
            checkSize(w, h, MAX_OUTPUT_PIXELS);
            const buffer = await streamRef.arrayBuffer();
            window.ormFullPacked = null; // stale export data must never be reused
            window.ormPacked = { width: w, height: h, bytes: new Uint8Array(buffer) };
        },

        setFullPacked: async function (width, height, streamRef) {
            const w = width | 0, h = height | 0;
            checkSize(w, h, MAX_OUTPUT_PIXELS);
            const buffer = await streamRef.arrayBuffer();
            window.ormFullPacked = { width: w, height: h, bytes: new Uint8Array(buffer) };
        },

        setSource: async function (width, height, streamRef) {
            const w = width | 0, h = height | 0;
            checkSize(w, h, MAX_OUTPUT_PIXELS);
            const buffer = await streamRef.arrayBuffer();
            window.ormSource = { width: w, height: h, bytes: new Uint8Array(buffer) };
        },

        renderPreview: function (tab) {
            if (!window.ormPacked) return '';
            if (previewFrame) cancelAnimationFrame(previewFrame);
            return new Promise(resolve => {
                previewFrame = requestAnimationFrame(() => {
                    try {
                        const { width, height, bytes } = window.ormPacked;
                        // Sample the preview down to 1024 px: a 4096² canvas + PNG encode
                        // is far too heavy for the UI thread; export still uses full data.
                        const scale = Math.min(1, 1024 / Math.max(width, height));
                        const outW = Math.max(1, Math.round(width * scale));
                        const outH = Math.max(1, Math.round(height * scale));
                        const canvas = canvasFor(outW, outH);
                        const context = canvas.getContext('2d');
                        const image = context.createImageData(outW, outH);
                        const target = image.data;
                        const rgb = tab === 'RGB' || tab === 'RGBA';
                        const component = tab === 'R' ? 2 : tab === 'G' ? 1 : tab === 'B' ? 0 : 3;
                        for (let y = 0; y < outH; y++) {
                            const sy = Math.min(Math.floor(y * height / outH), height - 1);
                            for (let x = 0; x < outW; x++) {
                                const sx = Math.min(Math.floor(x * width / outW), width - 1);
                                const src = (sy * width + sx) * 4;
                                const dst = (y * outW + x) * 4;
                                if (rgb) {
                                    // The packed buffer is BGRA: swap red and blue for the canvas.
                                    target[dst] = bytes[src + 2];
                                    target[dst + 1] = bytes[src + 1];
                                    target[dst + 2] = bytes[src];
                                    target[dst + 3] = tab === 'RGB' ? 255 : bytes[src + 3];
                                } else {
                                    const value = bytes[src + component];
                                    target[dst] = value;
                                    target[dst + 1] = value;
                                    target[dst + 2] = value;
                                    target[dst + 3] = 255;
                                }
                            }
                        }
                        context.putImageData(image, 0, 0);
                        canvas.toBlob(blob => {
                            if (!blob) { resolve(''); return; }
                            if (window.ormPreviewUrl) URL.revokeObjectURL(window.ormPreviewUrl);
                            window.ormPreviewUrl = URL.createObjectURL(blob);
                            resolve(window.ormPreviewUrl);
                        }, 'image/png');
                    } catch {
                        resolve('');
                    }
                });
            });
        },

        save: function (format, name) {
            const packed = window.ormFullPacked || window.ormPacked;
            if (!packed) return;
            const { width, height, bytes } = packed;
            if (format === 'bmp') {
                download(new Blob([bmpBytes(width, height, bytes)], { type: 'image/bmp' }), name);
                return;
            }
            if (format === 'tga') {
                download(new Blob([tgaBytes(width, height, bytes)], { type: 'image/x-tga' }), name);
                return;
            }
            const canvas = canvasFor(width, height);
            bgraToImageData(canvas.getContext('2d'), width, height, bytes);
            const type = format === 'jpeg' ? 'image/jpeg' : 'image/png';
            canvas.toBlob(blob => { if (blob) download(blob, name); }, type, 0.92);
        },

        saveChannels: function (baseName) {
            const source = window.ormSource;
            if (!source) return;
            const { width, height, bytes } = source;
            const components = [2, 1, 0, 3];
            const suffix = ['R', 'G', 'B', 'A'];
            const canvas = canvasFor(width, height);
            const context = canvas.getContext('2d');
            const image = context.createImageData(width, height);
            components.forEach((component, index) => {
                const target = image.data;
                for (let i = 0; i < target.length; i += 4) {
                    const value = bytes[i + component];
                    target[i] = value;
                    target[i + 1] = value;
                    target[i + 2] = value;
                    target[i + 3] = 255;
                }
                context.putImageData(image, 0, 0);
                canvas.toBlob(blob => { if (blob) download(blob, `${baseName}_${suffix[index]}.png`); }, 'image/png');
            });
        },

        setLanguage: function (language) {
            document.documentElement.lang = language === 'ru' ? 'ru' : 'en';
        },
        loadSession: function () {
            try { return localStorage.getItem('orm-packer-session') || ''; }
            catch { return ''; }
        },
        saveSession: function (json) {
            try { localStorage.setItem('orm-packer-session', json); }
            catch { /* storage can be disabled */ }
        }
    };
})();
