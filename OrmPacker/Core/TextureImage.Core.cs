namespace OrmPacker;

/// <summary>Decoded texture in BGRA format (8 bits per channel).</summary>
public sealed partial class TextureImage
{
    public string FileName { get; }
    public int Width { get; }
    public int Height { get; }

    public byte[] Pixels { get; }

    private TextureImage(string fileName, int width, int height, byte[] pixels)
    {
        FileName = fileName;
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public static TextureImage FromPixels(string fileName, int width, int height, byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        ArgumentNullException.ThrowIfNull(pixels);
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Width and height must be positive.");

        var expected = (long)width * height * 4;
        if (expected > int.MaxValue || pixels.Length != expected)
            throw new ArgumentException($"Expected {expected} BGRA bytes, received {pixels.Length}.", nameof(pixels));

        return new TextureImage(fileName, width, height, pixels);
    }

    // Integer BT.601 luma. Masks are data, so no gamma or color conversion is applied.
    internal static byte Luminance(byte b, byte g, byte r) =>
        (byte)((r * 299 + g * 587 + b * 114) / 1000);

    public byte ComponentAt(int x, int y, SourceComponent component)
    {
        var i = (y * Width + x) * 4;
        var b = Pixels[i];
        var g = Pixels[i + 1];
        var r = Pixels[i + 2];
        var a = Pixels[i + 3];

        return component switch
        {
            SourceComponent.Red => r,
            SourceComponent.Green => g,
            SourceComponent.Blue => b,
            SourceComponent.Alpha => a,
            _ => Luminance(b, g, r)
        };
    }

}
