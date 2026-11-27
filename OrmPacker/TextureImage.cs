using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace OrmPacker;

public sealed partial class TextureImage
{
    public static TextureImage Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Input file not found.", path);

        var extension = Path.GetExtension(path);
        if (extension.Equals(".tga", StringComparison.OrdinalIgnoreCase))
            return TgaDecoder.Load(path);
        if (extension.Equals(".exr", StringComparison.OrdinalIgnoreCase))
            return ExrDecoder.Load(path);

        using var bitmap = new Bitmap(path);
        return FromBitmap(bitmap, Path.GetFileName(path));
    }

    private static TextureImage FromBitmap(Bitmap bitmap, string fileName)
    {
        var width = bitmap.Width;
        var height = bitmap.Height;
        var pixels = new byte[width * height * 4];

        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            if (data.Stride == width * 4)
            {
                Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            }
            else
            {
                for (var y = 0; y < height; y++)
                {
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), pixels,
                        y * width * 4, width * 4);
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return new TextureImage(fileName, width, height, pixels);
    }

    /// <summary>Small channel thumbnail for a slot (nearest-neighbor sampling).</summary>
    public Bitmap CreateThumbnail(SourceComponent component, bool invert, int size)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, size, size),
            ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (var y = 0; y < size; y++)
            {
                var sy = Math.Min(y * Height / size, Height - 1);
                for (var x = 0; x < size; x++)
                {
                    var sx = Math.Min(x * Width / size, Width - 1);
                    var v = ComponentAt(sx, sy, component);
                    if (invert) v = (byte)(255 - v);

                    var off = y * data.Stride + x * 4;
                    Marshal.WriteByte(data.Scan0, off, v);
                    Marshal.WriteByte(data.Scan0, off + 1, v);
                    Marshal.WriteByte(data.Scan0, off + 2, v);
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
}
