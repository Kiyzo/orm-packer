namespace OrmPacker;

/// <summary>Map semantics inferred from the file name.</summary>
public enum MapKind
{
    Unknown,
    Occlusion,
    Roughness,
    Metallic,
    Smoothness,
    DetailMask,
    Height,
    Albedo,
    Alpha,
    Normal
}

public enum Channel { R, G, B, A }

/// <summary>Whether an alpha channel is required in the output.</summary>
public enum AlphaMode { None, Optional, Required }

public sealed class ChannelDef
{
    public string NameKey { get; }
    public string DisplayName => Localization.Get(NameKey);
    public MapKind Kind { get; }
    public byte DefaultValue { get; }

    /// <summary>Unknown means the engine does not use this channel (except in Custom).</summary>
    public bool HasMap => Kind != MapKind.Unknown || NameKey.StartsWith("channel.", StringComparison.Ordinal);

    public ChannelDef(string nameKey, MapKind kind, byte defaultValue = 255)
    {
        NameKey = nameKey;
        Kind = kind;
        DefaultValue = defaultValue;
    }
}

public sealed class Preset
{
    private static readonly char[] NameSeparators = { '_', '-', ' ', '.', '#', '+' };

    public string NameKey { get; }
    public string DisplayName => Localization.Get(NameKey);
    public string Suffix { get; }
    public AlphaMode AlphaMode { get; }
    public IReadOnlyDictionary<Channel, ChannelDef> Channels { get; }

    private Preset(string nameKey, string suffix, AlphaMode alphaMode,
        Dictionary<Channel, ChannelDef> channels)
    {
        NameKey = nameKey;
        Suffix = suffix;
        AlphaMode = alphaMode;
        Channels = channels;
    }

    /// <summary>Unreal / Godot / glTF / Blender: R=AO, G=Roughness, B=Metallic, A=Height (optional).</summary>
    public static readonly Preset Orm = new(
        "preset.orm", "_ORM", AlphaMode.Optional,
        new Dictionary<Channel, ChannelDef>
        {
            [Channel.R] = new("map.occlusion", MapKind.Occlusion),
            [Channel.G] = new("map.roughness", MapKind.Roughness),
            [Channel.B] = new("map.metallic", MapKind.Metallic),
            [Channel.A] = new("map.height", MapKind.Height)
        });

    /// <summary>Unity HDRP Mask Map: R=Metallic, G=AO, B=Detail Mask, A=Smoothness.</summary>
    public static readonly Preset UnityHdrp = new(
        "preset.hdrp", "_MaskMap", AlphaMode.Required,
        new Dictionary<Channel, ChannelDef>
        {
            [Channel.R] = new("map.metallic", MapKind.Metallic),
            [Channel.G] = new("map.occlusion", MapKind.Occlusion),
            [Channel.B] = new("map.detailMask", MapKind.DetailMask),
            [Channel.A] = new("map.smoothness", MapKind.Smoothness)
        });

    /// <summary>Unity URP / Built-in: R=G=B=Metallic, A=Smoothness.</summary>
    public static readonly Preset UnityUrp = new(
        "preset.urp", "_MetallicSmoothness", AlphaMode.Required,
        new Dictionary<Channel, ChannelDef>
        {
            [Channel.R] = new("map.metallic", MapKind.Metallic, 0),
            [Channel.G] = new("map.metallic", MapKind.Metallic, 0),
            [Channel.B] = new("map.metallic", MapKind.Metallic, 0),
            [Channel.A] = new("map.smoothness", MapKind.Smoothness)
        });

    /// <summary>Four arbitrary channels with no fixed name-based assignment.</summary>
    public static readonly Preset Custom = new(
        "preset.custom", "", AlphaMode.Required,
        new Dictionary<Channel, ChannelDef>
        {
            [Channel.R] = new("channel.r", MapKind.Unknown),
            [Channel.G] = new("channel.g", MapKind.Unknown),
            [Channel.B] = new("channel.b", MapKind.Unknown),
            [Channel.A] = new("channel.a", MapKind.Unknown)
        });

    public static readonly IReadOnlyList<Preset> All = new[] { Orm, UnityHdrp, UnityUrp, Custom };

    /// <summary>Determines the map kind from the file name.</summary>
    public static MapKind DetectKind(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();

        // Check trailing words first: "rusty_metal_04_nor_gl" is detected as Normal,
        // not as Metallic just because "metal" appears in the material name.
        var tokens = name.Split(NameSeparators, StringSplitOptions.RemoveEmptyEntries);
        for (var i = tokens.Length - 1; i >= Math.Max(0, tokens.Length - 3); i--)
        {
            var kind = MatchToken(tokens[i]);
            if (kind != MapKind.Unknown) return kind;
        }

        // Fallback for concatenated names such as "MetallicSmoothness".
        if (Has(name, "smooth", "gloss", "smth")) return MapKind.Smoothness;
        if (Has(name, "rough", "rgh")) return MapKind.Roughness;
        if (Has(name, "metal", "mtl")) return MapKind.Metallic;
        if (Has(name, "detail")) return MapKind.DetailMask;
        if (Has(name, "occlusion", "ambient", "ao", "occ")) return MapKind.Occlusion;
        if (Has(name, "height", "displace", "disp", "bump", "hm")) return MapKind.Height;
        if (Has(name, "opacity", "alpha", "trans")) return MapKind.Alpha;
        if (Has(name, "albedo", "basecolor", "diffuse", "_color", "_bc")) return MapKind.Albedo;

        return MapKind.Unknown;
    }

    /// <summary>
    /// Metallic and Detail Mask are discrete masks: nearest-neighbor resampling only.
    /// Other maps are continuous, where bilinear filtering preserves smooth transitions.
    /// </summary>
    public static ResampleFilter FilterFor(MapKind kind) =>
        kind is MapKind.Metallic or MapKind.DetailMask
            ? ResampleFilter.Nearest
            : ResampleFilter.Bilinear;

    public static bool IsIgnoredInBatch(MapKind kind) =>
        kind is MapKind.Normal or MapKind.Albedo or MapKind.Alpha or MapKind.Unknown;

    public static string KindDisplayName(MapKind kind) => kind switch
    {
        MapKind.DetailMask => Localization.Get("map.detailMask"),
        _ => Localization.Get("map." + kind.ToString().ToLowerInvariant())
    };

    /// <summary>All preset slots of a given map kind (URP: R=G=B=Metallic).</summary>
    public IReadOnlyList<Channel> ChannelsFor(MapKind kind) => Channels
        .Where(pair => pair.Value.HasMap && pair.Value.Kind == kind)
        .Select(pair => pair.Key).ToList();

    /// <summary>Slots whose maps form an inverse pair (Roughness ↔ Smoothness).</summary>
    public IReadOnlyList<Channel> InvertedChannelsFor(MapKind kind) => Channels
        .Where(pair => pair.Value.HasMap && IsInvertedPair(kind, pair.Value.Kind))
        .Select(pair => pair.Key).ToList();

    /// <summary>Roughness and Smoothness are inverses; preset changes take this into account.</summary>
    public static bool IsInvertedPair(MapKind from, MapKind to) =>
        (from == MapKind.Smoothness && to == MapKind.Roughness) ||
        (from == MapKind.Roughness && to == MapKind.Smoothness);

    private static MapKind MatchToken(string token) => token switch
    {
        "ao" or "occ" or "occlusion" or "ambient" or "ambientocclusion" => MapKind.Occlusion,
        "rough" or "roughness" or "rgh" => MapKind.Roughness,
        "metal" or "metallic" or "metalness" or "mtl" => MapKind.Metallic,
        "smooth" or "smoothness" or "gloss" or "glossiness" or "smth" => MapKind.Smoothness,
        "height" or "hm" or "disp" or "displace" or "displacement" or "bump" => MapKind.Height,
        "detail" or "detailmask" or "mask" => MapKind.DetailMask,
        "albedo" or "diff" or "diffuse" or "color" or "basecolor" or "bc" => MapKind.Albedo,
        "alpha" or "opacity" or "trans" => MapKind.Alpha,
        "nor" or "normal" or "nrm" or "gl" => MapKind.Normal,
        _ => MapKind.Unknown
    };

    private static bool Has(string name, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (name.Contains(token, StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
