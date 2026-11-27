using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace OrmPacker;

public static partial class Packer
{
    public const int PreviewMaxSide = 1024;

    private const int CheckerCellSize = 8;
    private const byte CheckerLight = 44;
    private const byte CheckerDark = 30;

    /// <summary>Renders the BGRA result for display; optionally composites alpha over a checkerboard so the A channel is visible.</summary>
    public static Bitmap ToBitmap(PackedTexture texture, bool showCheckerboard = false, bool forceOpaque = false)
    {
        var bitmap = new Bitmap(texture.Width, texture.Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, texture.Width, texture.Height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < texture.Height; y++)
            {
                for (var x = 0; x < texture.Width; x++)
                {
                    var src = (y * texture.Width + x) * 4;
                    var b = texture.Pixels[src];
                    var g = texture.Pixels[src + 1];
                    var r = texture.Pixels[src + 2];
                    var a = texture.Pixels[src + 3];
                    if (forceOpaque) a = 255;

                    if (showCheckerboard && a != 255)
                    {
                        var bg = (byte)(((x / CheckerCellSize + y / CheckerCellSize) & 1) == 0
                            ? CheckerLight : CheckerDark);
                        r = (byte)((r * a + bg * (255 - a)) / 255);
                        g = (byte)((g * a + bg * (255 - a)) / 255);
                        b = (byte)((b * a + bg * (255 - a)) / 255);
                        a = 255;
                    }

                    var off = y * data.Stride + x * 4;
                    Marshal.WriteByte(data.Scan0, off, b);
                    Marshal.WriteByte(data.Scan0, off + 1, g);
                    Marshal.WriteByte(data.Scan0, off + 2, r);
                    Marshal.WriteByte(data.Scan0, off + 3, a);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    /// <summary>A single output channel rendered as a grayscale preview.</summary>
    public static Bitmap ToChannelBitmap(PackedTexture texture, Channel channel)
    {
        var index = channel switch
        {
            Channel.R => 2,
            Channel.G => 1,
            Channel.B => 0,
            _ => 3
        };

        var bitmap = new Bitmap(texture.Width, texture.Height, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, texture.Width, texture.Height),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < texture.Height; y++)
            {
                for (var x = 0; x < texture.Width; x++)
                {
                    var value = texture.Pixels[(y * texture.Width + x) * 4 + index];
                    var off = y * data.Stride + x * 4;
                    Marshal.WriteByte(data.Scan0, off, value);
                    Marshal.WriteByte(data.Scan0, off + 1, value);
                    Marshal.WriteByte(data.Scan0, off + 2, value);
                    Marshal.WriteByte(data.Scan0, off + 3, 255);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

    public static void Save(PackedTexture texture, string path, SaveFormat format, bool alpha)
    {
        switch (format)
        {
            case SaveFormat.Png:
                SavePng(texture, path, alpha);
                break;
            case SaveFormat.Jpeg:
                SaveJpeg(texture, path);
                break;
            case SaveFormat.Tga:
                SaveTga(texture, path, alpha);
                break;
            case SaveFormat.Bmp:
                using (var bitmap = CreateBitmap(texture, alpha,
                           alpha ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb))
                    bitmap.Save(path, ImageFormat.Bmp);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    /// <summary>
    /// Lossless PNG: RGB, or RGBA when the target engine needs an alpha channel.
    /// GDI+ adds sRGB/gAMA/pHYs chunks — unnecessary (and incorrect) color metadata for masks.
    /// Only IHDR/IDAT/IEND are kept: the IDAT stream is not re-encoded, so pixel bytes stay identical.
    /// </summary>
    public static void SavePng(PackedTexture texture, string path, bool alpha)
    {
        using var bitmap = CreateBitmap(texture, alpha,
            alpha ? PixelFormat.Format32bppArgb : PixelFormat.Format24bppRgb);
        using var buffer = new MemoryStream();
        bitmap.Save(buffer, ImageFormat.Png);
        File.WriteAllBytes(path, StripPngMetadata(buffer.ToArray()));
    }

    private static byte[] StripPngMetadata(byte[] source)
    {
        var result = new List<byte>(source.Length);
        result.AddRange(source.Take(8)); // PNG signature
        var pos = 8;
        while (pos + 12 <= source.Length)
        {
            var length = (source[pos] << 24) | (source[pos + 1] << 16) |
                         (source[pos + 2] << 8) | source[pos + 3];
            var type = System.Text.Encoding.ASCII.GetString(source, pos + 4, 4);
            var total = 12 + length;
            if (pos + total > source.Length) break;
            if (type is "IHDR" or "IDAT" or "IEND") result.AddRange(source.Skip(pos).Take(total));
            pos += total;
            if (type == "IEND") break;
        }
        return result.ToArray();
    }

    /// <summary>JPEG is always RGB; default quality is 92 (masks are best stored as PNG).</summary>
    public static void SaveJpeg(PackedTexture texture, string path, long quality = 92)
    {
        var codec = ImageCodecInfo.GetImageEncoders()
            .FirstOrDefault(c => c.MimeType == "image/jpeg");
        if (codec is null) throw new InvalidOperationException("JPEG codec is not available on this system.");

        using var bitmap = CreateBitmap(texture, false, PixelFormat.Format24bppRgb);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        bitmap.Save(path, codec, parameters);
    }

    /// <summary>TGA type 2, uncompressed, top-left origin.</summary>
    public static void SaveTga(PackedTexture texture, string path, bool alpha)
    {
        var channels = alpha ? 4 : 3;
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(stream);

        writer.Write((byte)0);   // ID length
        writer.Write((byte)0);   // color map type
        writer.Write((byte)2);   // uncompressed truecolor
        for (var i = 0; i < 9; i++) writer.Write((byte)0); // color map spec + X/Y origin
        writer.Write((ushort)texture.Width);
        writer.Write((ushort)texture.Height);
        writer.Write((byte)(channels * 8));
        writer.Write((byte)(alpha ? 0x28 : 0x20)); // top-left origin, 8 alpha bits

        var row = new byte[texture.Width * channels];
        for (var y = 0; y < texture.Height; y++)
        {
            for (var x = 0; x < texture.Width; x++)
            {
                var src = (y * texture.Width + x) * 4;
                var dst = x * channels;
                row[dst] = texture.Pixels[src];
                row[dst + 1] = texture.Pixels[src + 1];
                row[dst + 2] = texture.Pixels[src + 2];
                if (alpha) row[dst + 3] = texture.Pixels[src + 3];
            }
            writer.Write(row);
        }
    }

    private static Bitmap CreateBitmap(PackedTexture texture, bool alpha, PixelFormat format)
    {
        // alpha must match format's channel count (Format32bppArgb ↔ true, Format24bppRgb ↔ false):
        // it drives bytesPerPixel, while GDI+ writes according to format.
        var bytesPerPixel = alpha ? 4 : 3;
        var bitmap = new Bitmap(texture.Width, texture.Height, format);
        var data = bitmap.LockBits(new Rectangle(0, 0, texture.Width, texture.Height),
            ImageLockMode.WriteOnly, format);
        try
        {
            for (var y = 0; y < texture.Height; y++)
            {
                var dst = y * data.Stride;
                for (var x = 0; x < texture.Width; x++, dst += bytesPerPixel)
                {
                    var src = (y * texture.Width + x) * 4;
                    Marshal.WriteByte(data.Scan0, dst, texture.Pixels[src]);
                    Marshal.WriteByte(data.Scan0, dst + 1, texture.Pixels[src + 1]);
                    Marshal.WriteByte(data.Scan0, dst + 2, texture.Pixels[src + 2]);
                    if (alpha) Marshal.WriteByte(data.Scan0, dst + 3, texture.Pixels[src + 3]);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
        return bitmap;
    }

}
