namespace OrmPacker;

public static partial class Packer
{
    // Fixed-point interpolation weight: 256 steps, +128 rounds to the nearest byte.
    private const int FractionOne = 256;

    /// <summary>
    /// Assembles an RGB(A) map into a BGRA buffer. Image-backed channels are resampled to the
    /// output size; missing channels and unused alpha are filled with a constant. The bilinear
    /// path buffers one single-channel image of source height × output width, never a full BGRA copy.
    /// </summary>
    public static PackedTexture Pack(int width, int height,
        ChannelInput r, ChannelInput g, ChannelInput b, ChannelInput a, bool useAlpha)
    {
        var result = new byte[checked(width * height * 4)];
        WriteChannel(result, width, height, 2, r);
        WriteChannel(result, width, height, 1, g);
        WriteChannel(result, width, height, 0, b);
        if (useAlpha) WriteChannel(result, width, height, 3, a);
        else Fill(result, width, height, 3, 255);
        return new PackedTexture(width, height, result);
    }

    private static void Fill(byte[] dest, int width, int height, int offset, byte value)
    {
        for (var y = 0; y < height; y++)
        {
            var start = y * width * 4 + offset;
            var end = start + width * 4;
            for (var i = start; i < end; i += 4) dest[i] = value;
        }
    }

    private static void WriteChannel(byte[] dest, int width, int height, int offset, ChannelInput input)
    {
        if (input.Image is null)
        {
            Fill(dest, width, height, offset, Apply(input.SolidValue, input.Invert));
            return;
        }

        var image = input.Image;
        var sourceOffset = input.Source switch
        {
            SourceComponent.Red => 2,
            SourceComponent.Green => 1,
            SourceComponent.Blue => 0,
            SourceComponent.Alpha => 3,
            _ => -1
        };

        if (image.Width == width && image.Height == height)
        {
            ForRows(height, y =>
            {
                var sourceRow = y * image.Width * 4;
                var target = y * width * 4 + offset;
                for (var x = 0; x < width; x++)
                {
                    var value = ReadSource(image, sourceRow + x * 4, sourceOffset, input.Source);
                    dest[target + x * 4] = Apply(value, input.Invert);
                }
            });
            return;
        }

        if (input.Filter == ResampleFilter.Nearest)
        {
            var across = NearestMap(image.Width, width);
            var down = NearestMap(image.Height, height);
            ForRows(height, y =>
            {
                var sourceRow = down[y] * image.Width * 4;
                var target = y * width * 4 + offset;
                for (var x = 0; x < width; x++)
                {
                    var value = ReadSource(image, sourceRow + across[x] * 4, sourceOffset, input.Source);
                    dest[target + x * 4] = Apply(value, input.Invert);
                }
            });
            return;
        }

        var acrossMap = SampleMap(image.Width, width);
        var downMap = SampleMap(image.Height, height);

        // Separable bilinear with fixed-point weights: one horizontal pass per
        // source row, then one vertical pass per output row. Upscaling 1024² →
        // 8192² is far cheaper than four source reads per output pixel.
        if ((long)image.Height * width > int.MaxValue)
            throw new ArgumentException("The source image is too large to resample.");
        var horizontal = new byte[image.Height * width];
        for (var sy = 0; sy < image.Height; sy++)
        {
            var sourceRow = sy * image.Width * 4;
            var targetRow = sy * width;
            for (var x = 0; x < width; x++)
            {
                var first = ReadSource(image, sourceRow + acrossMap.First[x] * 4, sourceOffset, input.Source);
                var second = ReadSource(image, sourceRow + acrossMap.Second[x] * 4, sourceOffset, input.Source);
                horizontal[targetRow + x] = Lerp(first, second, acrossMap.Weight[x]);
            }
        }

        ForRows(height, y =>
        {
            var row0 = downMap.First[y] * width;
            var row1 = downMap.Second[y] * width;
            var target = y * width * 4 + offset;
            var weight = downMap.Weight[y];
            for (var x = 0; x < width; x++)
            {
                var value = Lerp(horizontal[row0 + x], horizontal[row1 + x], weight);
                dest[target + x * 4] = Apply(value, input.Invert);
            }
        });
    }

    private static int[] NearestMap(int sourceSize, int targetSize)
    {
        var indices = new int[targetSize];
        var scale = (double)sourceSize / targetSize;
        for (var i = 0; i < targetSize; i++)
            indices[i] = Math.Clamp((int)Math.Floor((i + 0.5) * scale), 0, sourceSize - 1);
        return indices;
    }

    private static (int[] First, int[] Second, int[] Weight) SampleMap(int sourceSize, int targetSize)
    {
        var first = new int[targetSize];
        var second = new int[targetSize];
        var weight = new int[targetSize];
        var scale = (double)sourceSize / targetSize;
        var last = sourceSize - 1;
        for (var i = 0; i < targetSize; i++)
        {
            var position = Math.Clamp((i + 0.5) * scale - 0.5, 0, last);
            var a = (int)position;
            first[i] = a;
            second[i] = Math.Min(a + 1, last);
            weight[i] = (int)((position - a) * FractionOne + 0.5);
        }
        return (first, second, weight);
    }

    private static byte Lerp(byte first, byte second, int weight) =>
        (byte)((first * (FractionOne - weight) + second * weight + FractionOne / 2) >> 8);

    private static byte ReadSource(TextureImage image, int index, int sourceOffset,
        SourceComponent component) =>
        sourceOffset >= 0 ? image.Pixels[index + sourceOffset] : Sample(image, index, component);

    private static byte Sample(TextureImage image, int index, SourceComponent component)
    {
        var b = image.Pixels[index];
        var g = image.Pixels[index + 1];
        var r = image.Pixels[index + 2];
        return component switch
        {
            SourceComponent.Red => r,
            SourceComponent.Green => g,
            SourceComponent.Blue => b,
            SourceComponent.Alpha => image.Pixels[index + 3],
            _ => TextureImage.Luminance(b, g, r)
        };
    }

    private static byte Apply(byte value, bool invert) => invert ? (byte)(255 - value) : value;

    // Browser WASM has no useful thread parallelism: plain loops avoid the overhead.
    private static void ForRows(int height, Action<int> body)
    {
#if ORM_WEB
        for (var y = 0; y < height; y++) body(y);
#else
        Parallel.For(0, height, body);
#endif
    }
}
