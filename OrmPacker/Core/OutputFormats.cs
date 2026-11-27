namespace OrmPacker;

public enum SaveFormat { Png, Jpeg, Tga, Bmp }

/// <summary>Output format metadata: encoder, extension, dialog filter, and alpha support.</summary>
public sealed class OutputFormat
{
    public SaveFormat Format { get; }
    public string Name { get; }
    public string Extension { get; }
    public string Filter { get; }
    public bool SupportsAlpha { get; }
    /// <summary>Short label for the closed list (so it does not overlap the dropdown arrow).</summary>
    public string DisplayName => Name;
    /// <summary>Full name shown in the drop-down list.</summary>
    public string DescriptionText => Localization.Get("format." + Name.ToLowerInvariant());

    private OutputFormat(SaveFormat format, string name, string extension, string filter, bool supportsAlpha)
    {
        Format = format;
        Name = name;
        Extension = extension;
        Filter = filter;
        SupportsAlpha = supportsAlpha;
    }

    public static readonly OutputFormat Png = new(
        SaveFormat.Png, "PNG", ".png", "PNG (*.png)|*.png", true);
    public static readonly OutputFormat Jpeg = new(
        SaveFormat.Jpeg, "JPEG", ".jpg", "JPEG (*.jpg)|*.jpg;*.jpeg", false);
    public static readonly OutputFormat Tga = new(
        SaveFormat.Tga, "TGA", ".tga", "TGA (*.tga)|*.tga", true);
    public static readonly OutputFormat Bmp = new(
        SaveFormat.Bmp, "BMP", ".bmp", "BMP (*.bmp)|*.bmp", true);

    public static readonly IReadOnlyList<OutputFormat> All = new[] { Png, Jpeg, Tga, Bmp };
}
