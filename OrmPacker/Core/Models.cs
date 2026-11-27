namespace OrmPacker;

/// <summary>Channel resampling: bilinear for continuous maps, nearest for ID-style masks.</summary>
public enum ResampleFilter { Bilinear, Nearest }

/// <summary>A single source channel: either an image or a constant value.</summary>
public readonly record struct ChannelInput(
    TextureImage? Image, SourceComponent Source, byte SolidValue, bool Invert,
    ResampleFilter Filter = ResampleFilter.Bilinear);

public sealed record PackedTexture(int Width, int Height, byte[] Pixels);

public enum SourceComponent { Red, Green, Blue, Alpha, Luminance }
