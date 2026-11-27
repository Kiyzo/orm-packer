using ImageMagick;

namespace OrmPacker;

/// <summary>
/// Desktop EXR decoding goes through ImageMagick (DWAA/PIZ/ZIP, etc.), which exposes
/// 16-bit samples. Masks are linear data, so the 0..65535 quantum range is reduced to
/// 0..255 with no gamma or sRGB conversion.
/// </summary>
internal static class ExrDecoder
{
    public static TextureImage Load(string path)
    {
        using var image = new MagickImage(path);
        var width = checked((int)image.Width);
        var height = checked((int)image.Height);

        var source = image.GetPixels();
        var channels = (int)source.Channels;
        var red = (int)(source.GetChannelIndex(PixelChannel.Red) ?? 0u);
        var green = (int)(source.GetChannelIndex(PixelChannel.Green) ?? (uint)red);
        var blue = (int)(source.GetChannelIndex(PixelChannel.Blue) ?? (uint)red);
        int? alpha = source.GetChannelIndex(PixelChannel.Alpha) is uint index ? (int)index : null;

        var raw = source.GetValues()!; // interleaved in ImageMagick's channel order; single-channel files expose one sample per pixel
        var count = checked(width * height);
        var pixels = new byte[checked(count * 4)];

        for (var i = 0; i < count; i++)
        {
            var s = i * channels;
            var dst = i * 4;
            pixels[dst] = ToByte(raw[s + blue]);
            pixels[dst + 1] = ToByte(raw[s + green]);
            pixels[dst + 2] = ToByte(raw[s + red]);
            pixels[dst + 3] = alpha is null ? (byte)255 : ToByte(raw[s + alpha.Value]);
        }

        return TextureImage.FromPixels(Path.GetFileName(path), width, height, pixels);
    }

    /// <summary>16-bit → 8-bit: clamp to 0..255 and round halves up (away from zero), with no gamma conversion.</summary>
    private static byte ToByte(ushort value) =>
        (byte)Math.Clamp((int)Math.Round(value / 257.0, MidpointRounding.AwayFromZero), 0, 255);
}
