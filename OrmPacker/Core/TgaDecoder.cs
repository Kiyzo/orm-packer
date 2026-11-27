namespace OrmPacker;

/// <summary>
/// Minimal TGA decoder: 8/16/24/32 bpp, paletted or not, RLE (types 1, 2, 3, 9, 10, 11).
/// Exports from Blender/Substance/Unreal almost always fall into this set.
/// </summary>
public static class TgaDecoder
{
    public static TextureImage Load(string path) => Decode(File.ReadAllBytes(path), Path.GetFileName(path));

    public static TextureImage Decode(byte[] file, string fileName)
    {
        if (file.Length < 18)
            throw new InvalidDataException("TGA: file is too short.");

        int idLength = file[0];
        int colorMapType = file[1];
        int imageType = file[2];
        var cmLength = file[5] | (file[6] << 8);
        int cmDepth = file[7];
        var width = file[12] | (file[13] << 8);
        var height = file[14] | (file[15] << 8);
        int bpp = file[16];
        int descriptor = file[17];

        if (width <= 0 || height <= 0 || (long)width * height * 4 > int.MaxValue)
            throw new InvalidDataException("TGA: invalid image dimensions.");

        var rle = imageType is 9 or 10 or 11;
        var gray = imageType is 3 or 11;
        var mapped = imageType is 1 or 9;
        if (!rle && imageType != 1 && imageType != 2 && imageType != 3)
            throw new NotSupportedException($"TGA: image type {imageType} is not supported.");
        if (!gray && !mapped && bpp is not (16 or 24 or 32))
            throw new NotSupportedException($"TGA: {bpp} bpp is not supported.");

        // Color palette (if present)
        var palette = Array.Empty<byte>();
        var paletteEntrySize = (cmDepth + 7) / 8;
        var pos = 18 + idLength;
        if (colorMapType == 1)
        {
            if (cmDepth is not (24 or 32))
                throw new NotSupportedException($"TGA: {cmDepth}-bpp palette is not supported.");

            palette = new byte[cmLength * paletteEntrySize];
            Ensure(file, pos, palette.Length);
            Array.Copy(file, pos, palette, 0, palette.Length);
            pos += palette.Length;
        }
        else if (mapped)
        {
            throw new InvalidDataException("TGA: paletted image without a color table.");
        }

        var topOrigin = (descriptor & 0x20) != 0;
        var pixels = new byte[width * height * 4];

        for (var y = 0; y < height; y++)
        {
            var destY = topOrigin ? y : height - 1 - y;
            for (var x = 0; x < width;)
            {
                var count = 1;
                var repeat = false;
                if (rle)
                {
                    Ensure(file, pos, 1);
                    int packet = file[pos++];
                    count = (packet & 0x7F) + 1;
                    repeat = (packet & 0x80) != 0;
                }

                byte b = 0, g = 0, r = 0, a = 255;
                if (repeat)
                    ReadPixel(file, ref pos, bpp, gray, mapped, palette, paletteEntrySize, out b, out g, out r, out a);

                for (var k = 0; k < count && x < width; k++)
                {
                    if (!repeat)
                        ReadPixel(file, ref pos, bpp, gray, mapped, palette, paletteEntrySize, out b, out g, out r, out a);

                    var dst = (destY * width + x) * 4;
                    pixels[dst] = b;
                    pixels[dst + 1] = g;
                    pixels[dst + 2] = r;
                    pixels[dst + 3] = a;
                    x++;
                }
            }
        }

        return TextureImage.FromPixels(fileName, width, height, pixels);
    }

    private static void ReadPixel(byte[] file, ref int pos, int bpp, bool gray, bool mapped,
        byte[] palette, int paletteSize, out byte b, out byte g, out byte r, out byte a)
    {
        b = g = r = 0;
        a = 255;

        if (mapped)
        {
            Ensure(file, pos, 1);
            var p = file[pos++] * paletteSize;
            if (p + 2 < palette.Length)
            {
                b = palette[p];
                g = palette[p + 1];
                r = palette[p + 2];
                if (paletteSize == 4) a = palette[p + 3];
            }
            return;
        }

        if (gray)
        {
            // TGA grayscale 16bpp = 8 bits luminance + 8 bits alpha (low byte is luminance).
            Ensure(file, pos, bpp == 16 ? 2 : 1);
            var v = file[pos++];
            if (bpp == 16) a = file[pos++];
            b = g = r = v;
            return;
        }

        switch (bpp)
        {
            case 16:
            {
                Ensure(file, pos, 2);
                var value = (ushort)(file[pos] | (file[pos + 1] << 8));
                pos += 2;
                r = Scale5((value >> 10) & 0x1F);
                g = Scale5((value >> 5) & 0x1F);
                b = Scale5(value & 0x1F);
                break;
            }
            case 24:
                Ensure(file, pos, 3);
                b = file[pos]; g = file[pos + 1]; r = file[pos + 2];
                pos += 3;
                break;
            case 32:
                Ensure(file, pos, 4);
                b = file[pos]; g = file[pos + 1]; r = file[pos + 2]; a = file[pos + 3];
                pos += 4;
                break;
        }
    }

    /// <summary>5-bit → 8-bit: round rather than truncate.</summary>
    private static byte Scale5(int value) =>
        (byte)Math.Round(value * 255.0 / 31, MidpointRounding.AwayFromZero);

    private static void Ensure(byte[] file, int pos, int count)
    {
        if (pos < 0 || pos + count > file.Length)
            throw new InvalidDataException("TGA: corrupted image data.");
    }
}
