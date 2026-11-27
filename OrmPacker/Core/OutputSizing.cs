namespace OrmPacker;

/// <summary>Picks the largest loaded map by area as the reference size; all channels are resampled to it.</summary>
public static class OutputSizing
{
    public static (int Width, int Height) SourceSize(params ChannelInput[] inputs)
    {
        TextureImage? best = null;
        long bestArea = 0;
        foreach (var input in inputs)
        {
            var image = input.Image;
            if (image is null) continue;
            var area = (long)image.Width * image.Height;
            if (area <= bestArea) continue;
            best = image;
            bestArea = area;
        }
        return best is null ? (0, 0) : (best.Width, best.Height);
    }

    public static (int Width, int Height) TargetSize(OutputSize size, params ChannelInput[] inputs)
    {
        (var width, var height) = SourceSize(inputs);
        return size.Apply(width, height);
    }
}
