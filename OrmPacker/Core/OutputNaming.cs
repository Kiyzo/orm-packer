using System.Text;

namespace OrmPacker;

/// <summary>
/// Suggests an output name based on the loaded files: common words plus removal of technical
/// suffixes (_AO, _Roughness, _1k, _normal, etc.) to keep the name readable.
/// </summary>
public static class OutputNaming
{
    private static readonly HashSet<string> Noise = new(StringComparer.OrdinalIgnoreCase)
    {
        "ao", "occ", "occlusion", "ambient", "rough", "roughness", "rgh",
        "metal", "metallic", "metalness", "mtl",
        "smooth", "smoothness", "gloss", "glossiness", "smth",
        "height", "hm", "disp", "displace", "displacement", "bump",
        "detail", "detailmask", "mask", "albedo", "diff", "diffuse", "color", "basecolor", "bc",
        "normal", "nor", "nrm", "gl", "dx",
        "alpha", "opacity", "transparency", "trans",
        "map", "texture", "tex", "pbr", "srgb", "linear",
        "1k", "2k", "4k", "8k", "16k", "512", "1024", "2048", "4096", "8192"
    };

    public static string Suggest(IReadOnlyList<string> paths, string suffix, string extension = ".png")
    {
        if (paths.Count == 0) return "Texture" + suffix + extension;

        var names = paths.Select(p => Path.GetFileNameWithoutExtension(p)).ToList();

        var stem = CommonPrefix(names);
        stem = TrimNoise(stem);
        if (stem.Length < 2)
        {
            var tokens = names.Select(SplitTokens).ToList();
            var common = tokens[0].Where(t => !Noise.Contains(t) &&
                                              tokens.All(list => list.Contains(t, StringComparer.OrdinalIgnoreCase))).ToList();
            if (common.Count == 0)
                common = tokens[0].Where(t => !Noise.Contains(t)).ToList();
            stem = string.Join("_", common);
        }

        if (stem.Length == 0) stem = "Texture";
        return stem + suffix + extension;
    }

    /// <summary>Handles name collisions by appending _01, _02, ... to the suggested file name.</summary>
    public static string EnsureUnique(string name, string? directory = null)
    {
        var root = directory ?? Path.GetDirectoryName(name) ?? Environment.CurrentDirectory;
        var full = Path.IsPathRooted(name) ? name : Path.Combine(root, name);
        if (!File.Exists(full)) return name;

        var stem = Path.GetFileNameWithoutExtension(full);
        var extension = Path.GetExtension(full);
        for (var i = 1; i <= 999; i++)
        {
            var candidate = Path.Combine(root, $"{stem}_{i:D2}{extension}");
            if (!File.Exists(candidate))
                return Path.IsPathRooted(name) ? candidate : Path.GetFileName(candidate);
        }
        return name;
    }

    private static string CommonPrefix(List<string> names)
    {
        var prefix = names[0];
        foreach (var name in names)
        {
            var i = 0;
            while (i < prefix.Length && i < name.Length &&
                   char.ToUpperInvariant(prefix[i]) == char.ToUpperInvariant(name[i])) i++;
            prefix = prefix[..i];
            if (prefix.Length == 0) break;
        }
        return prefix.TrimEnd(' ', '_', '-', '.', '#');
    }

    /// <summary>Removes trailing tokens such as "_1k", "_ao", "_normal".</summary>
    private static string TrimNoise(string stem)
    {
        while (true)
        {
            var tokens = SplitTokens(stem);
            if (tokens.Count == 0 || !Noise.Contains(tokens[^1])) break;
            tokens.RemoveAt(tokens.Count - 1);
            stem = string.Join("_", tokens);
        }
        return stem;
    }

    private static List<string> SplitTokens(string name)
    {
        var result = new List<string>();
        var buffer = new StringBuilder();
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c)) buffer.Append(c);
            else if (buffer.Length > 0) { result.Add(buffer.ToString()); buffer.Clear(); }
        }
        if (buffer.Length > 0) result.Add(buffer.ToString());
        return result;
    }
}
