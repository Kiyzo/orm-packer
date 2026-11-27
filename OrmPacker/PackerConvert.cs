using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace OrmPacker;

public static partial class PackerConvert
{
    /// <summary>Saves the source's four channels as separate grayscale PNGs (_R/_G/_B/_A).</summary>
    public static IReadOnlyList<string> UnpackChannels(TextureImage source, string directory, string baseName)
    {
        Directory.CreateDirectory(directory);
        var paths = new List<string>(4);
        foreach (var channel in new[] { Channel.R, Channel.G, Channel.B, Channel.A })
        {
            var path = Path.Combine(directory, baseName + "_" + channel + ".png");
            using var bitmap = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            var data = bitmap.LockBits(new Rectangle(0, 0, source.Width, source.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                // Channel and SourceComponent share the R/G/B/A ordinals, so the cast maps slot → component.
                var component = (SourceComponent)channel;
                for (var y = 0; y < source.Height; y++)
                for (var x = 0; x < source.Width; x++)
                {
                    var value = source.ComponentAt(x, y, component);
                    var offset = y * data.Stride + x * 4;
                    Marshal.WriteByte(data.Scan0, offset, value);
                    Marshal.WriteByte(data.Scan0, offset + 1, value);
                    Marshal.WriteByte(data.Scan0, offset + 2, value);
                    Marshal.WriteByte(data.Scan0, offset + 3, 255);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }
            bitmap.Save(path, ImageFormat.Png);
            paths.Add(path);
        }
        return paths;
    }

}
