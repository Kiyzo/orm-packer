namespace OrmPacker;

/// <summary>Length of the output's long side; null = original (size of the loaded maps).</summary>
public sealed class OutputSize
{
    public int? Pixels { get; }
    public string DisplayName => Pixels is null
        ? Localization.Get("size.original")
        : $"{Pixels}×{Pixels}" + (Pixels == Default.Pixels ? Localization.Get("size.default") : "");

    private OutputSize(int? pixels) => Pixels = pixels;

    public static readonly OutputSize Original = new(null);
    public static readonly OutputSize Default = new(1024);

    public static readonly IReadOnlyList<OutputSize> All = new[]
    {
        Original,
        new OutputSize(8192),
        new OutputSize(4096),
        new OutputSize(2048),
        Default,
        new OutputSize(512),
        new OutputSize(256),
        new OutputSize(128),
        new OutputSize(64)
    };

    /// <summary>Preserves the aspect ratio of the source maps.</summary>
    public (int Width, int Height) Apply(int sourceWidth, int sourceHeight)
    {
        if (Pixels is null || sourceWidth == 0 || sourceHeight == 0) return (sourceWidth, sourceHeight);
        var scale = Pixels.Value / (double)Math.Max(sourceWidth, sourceHeight);
        return (Math.Max(1, (int)Math.Round(sourceWidth * scale)),
                Math.Max(1, (int)Math.Round(sourceHeight * scale)));
    }
}
