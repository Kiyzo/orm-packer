using System.Reflection;
using System.Runtime.InteropServices;

namespace OrmPacker;

/// <summary>
/// Headless commands built on the same core as the GUI. No new dependencies or network calls.
/// </summary>
public static class Cli
{
    private static readonly Dictionary<string, MapKind> MapOptions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["--ao"] = MapKind.Occlusion,
        ["--occ"] = MapKind.Occlusion,
        ["--occlusion"] = MapKind.Occlusion,
        ["--rough"] = MapKind.Roughness,
        ["--roughness"] = MapKind.Roughness,
        ["--smooth"] = MapKind.Smoothness,
        ["--smoothness"] = MapKind.Smoothness,
        ["--metal"] = MapKind.Metallic,
        ["--metallic"] = MapKind.Metallic,
        ["--detail"] = MapKind.DetailMask,
        ["--height"] = MapKind.Height
    };

    public static bool IsCommand(string[] args) =>
        (args.Length > 0 && args[0] is "pack" or "convert" or "batch" or "unpack") || IsHelpRequest(args);

    public static bool IsHelpRequest(string[] args) =>
        args.Length > 0 && args[0] is "--help" or "-h" or "help";

    public static bool IsVersionRequest(string[] args) =>
        args.Length > 0 && args[0] is "--version" or "-v" or "version";

    public static string VersionText()
    {
        var assembly = typeof(Cli).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";
        return $"ORM Packer {informational.Split('+')[0]}";
    }

    /// <summary>A WinExe has no console of its own: attach to the calling process's console.</summary>
    public static void AttachParentConsole()
    {
        try { AttachConsole(-1); }
        catch (DllNotFoundException) { /* launched from Explorer — run silently without output */ }
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);

    public static int Run(string[] args, TextWriter output)
    {
        try
        {
            var options = ParseOptions(args);
            return args[0] switch
            {
                "pack" => PackCommand(options, output),
                "convert" => ConvertCommand(options, output),
                "batch" => BatchCommand(options, output),
                "unpack" => UnpackCommand(options, output),
                "--help" or "-h" or "help" => Help(output),
                _ => Usage(output)
            };
        }
        catch (Exception ex)
        {
            output.WriteLine($"error: {ex.Message}");
            return 1;
        }
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Pairs are "--flag value"; skip the command at 0 and never treat the final token as a flag.
        for (var i = 1; i < args.Length - 1; i++)
        {
            if (args[i].StartsWith("--", StringComparison.Ordinal))
                result[args[i]] = args[++i];
        }
        return result;
    }

    private static int Help(TextWriter output)
    {
        Usage(output);
        return 0;
    }

    private static int Usage(TextWriter output)
    {
        output.WriteLine("OrmPacker 8-bit PBR mask packer");
        output.WriteLine("  pack    --preset <orm|hdrp|urp|custom> --ao ... --rough ... --metal ... --out ... [--size N] [--format png|jpg|tga|bmp]");
        output.WriteLine("  convert --from <id> --to <id> --in <packed-map> --out ... [--size N] [--format ...]");
        output.WriteLine("  batch   --in <sets-dir> --out <dir> --preset <id> [--size N] [--format ...]");
        output.WriteLine("  unpack  --in <packed-map> --out <dir> [--name <base>]");
        output.WriteLine("  --version   print version and exit");
        return 2;
    }

    private static Preset PresetOf(Dictionary<string, string> options, string key) =>
        SessionStore.PresetById(options.GetValueOrDefault(key));

    private static OutputSize SizeOf(Dictionary<string, string> options)
    {
        var value = options.GetValueOrDefault("--size");
        if (value is null || value.Equals("original", StringComparison.OrdinalIgnoreCase))
            return OutputSize.Original;
        if (int.TryParse(value, out var pixels))
        {
            var found = OutputSize.All.FirstOrDefault(s => s.Pixels == pixels);
            if (found is not null) return found;
        }
        throw new ArgumentException($"Unknown size: {value}");
    }

    private static OutputFormat FormatOf(Dictionary<string, string> options, string outPath)
    {
        var value = options.GetValueOrDefault("--format");
        if (value is not null)
        {
            var found = OutputFormat.All.FirstOrDefault(f =>
                f.Name.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                f.Extension.Equals("." + value.TrimStart('.'), StringComparison.OrdinalIgnoreCase));
            if (found is not null) return found;
            throw new ArgumentException($"Unknown format: {value}");
        }
        var extension = Path.GetExtension(outPath);
        return OutputFormat.All.FirstOrDefault(f =>
            f.Extension.Equals(extension, StringComparison.OrdinalIgnoreCase)) ?? OutputFormat.Png;
    }

    private static ChannelInput[] InputsFromFiles(Preset preset, IReadOnlyDictionary<MapKind, string> files)
    {
        var inputs = new ChannelInput[4];
        foreach (var channel in new[] { Channel.R, Channel.G, Channel.B, Channel.A })
        {
            var solid = preset.Channels[channel].DefaultValue;
            inputs[(int)channel] = new ChannelInput(null, SourceComponent.Red, solid, false,
                Preset.FilterFor(preset.Channels[channel].Kind));
        }

        foreach ((var kind, var path) in files)
        {
            var image = TextureImage.Load(path);
            var targets = preset.ChannelsFor(kind);
            var invert = false;
            if (targets.Count == 0)
            {
                targets = preset.InvertedChannelsFor(kind);
                invert = targets.Count > 0;
            }
            foreach (var channel in targets)
            {
                inputs[(int)channel] = new ChannelInput(image, SourceComponent.Red,
                    preset.Channels[channel].DefaultValue, invert,
                    Preset.FilterFor(preset.Channels[channel].Kind));
            }
        }
        return inputs;
    }

    private static void Write(PackedTexture packed, string path, OutputFormat format, bool alpha,
        TextWriter output)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        Packer.Save(packed, path, format.Format, alpha && format.SupportsAlpha);
        output.WriteLine($"saved: {path} ({packed.Width}x{packed.Height})");
    }

    private static int PackCommand(Dictionary<string, string> options, TextWriter output)
    {
        var preset = PresetOf(options, "--preset");
        var outPath = options["--out"];
        var files = new Dictionary<MapKind, string>();
        foreach ((var key, var path) in options)
            if (MapOptions.TryGetValue(key, out var kind)) files[kind] = path;
        if (files.Count == 0) throw new ArgumentException("No input maps (--ao/--rough/--metal/...).");

        var inputs = InputsFromFiles(preset, files);
        var size = SizeOf(options);
        (var width, var height) = OutputSizing.TargetSize(size, inputs);
        var alpha = PackerConvert.NeedsAlpha(preset, inputs);
        var packed = Packer.Pack(width, height, inputs[0], inputs[1], inputs[2], inputs[3], alpha);
        Write(packed, outPath, FormatOf(options, outPath), alpha, output);
        return 0;
    }

    private static int ConvertCommand(Dictionary<string, string> options, TextWriter output)
    {
        var from = PresetOf(options, "--from");
        var to = PresetOf(options, "--to");
        var source = TextureImage.Load(options["--in"]);
        var outPath = options["--out"];
        var inputs = PackerConvert.BuildInputs(source, from, to);
        var packed = PackerConvert.Convert(source, from, to, SizeOf(options), inputs, out var alpha);
        Write(packed, outPath, FormatOf(options, outPath), alpha, output);
        return 0;
    }

    private static int UnpackCommand(Dictionary<string, string> options, TextWriter output)
    {
        var inPath = options["--in"];
        var outDir = options["--out"];
        var source = TextureImage.Load(inPath);
        var name = options.GetValueOrDefault("--name") ?? Path.GetFileNameWithoutExtension(inPath);
        foreach (var path in PackerConvert.UnpackChannels(source, outDir, name))
            output.WriteLine($"saved: {path}");
        return 0;
    }

    private static int BatchCommand(Dictionary<string, string> options, TextWriter output)
    {
        var inputDir = options["--in"];
        var outputDir = options["--out"];
        var preset = PresetOf(options, "--preset");
        var size = SizeOf(options);
        var format = FormatOf(options, "");
        Directory.CreateDirectory(outputDir);
        var saved = 0;
        var skipped = new List<string>();

        var sets = Directory.Exists(inputDir) && Directory.GetDirectories(inputDir).Length > 0
            ? Directory.GetDirectories(inputDir).Select(d => (Name: Path.GetFileName(d), Path: d)).ToList()
            : new[] { (Name: Path.GetFileName(inputDir.TrimEnd('\\', '/')), Path: inputDir) }.ToList();

        foreach ((var name, var path) in sets)
        {
            var files = new Dictionary<MapKind, string>();
            foreach (var file in Directory.GetFiles(path))
            {
                var kind = Preset.DetectKind(file);
                if (Preset.IsIgnoredInBatch(kind)) continue;
                if (!MapOptions.Values.Contains(kind)) continue;
                files[kind] = file;
            }
            if (files.Count == 0) { skipped.Add(name); continue; }

            var inputs = InputsFromFiles(preset, files);
            (var width, var height) = OutputSizing.TargetSize(size, inputs);
            var alpha = PackerConvert.NeedsAlpha(preset, inputs);
            var packed = Packer.Pack(width, height, inputs[0], inputs[1], inputs[2], inputs[3], alpha);
            var fileName = OutputNaming.EnsureUnique(
                OutputNaming.Suggest(files.Values.ToList(), preset.Suffix, format.Extension), outputDir);
            var outPath = Path.Combine(outputDir, fileName);
            Packer.Save(packed, outPath, format.Format, alpha && format.SupportsAlpha);
            output.WriteLine($"saved: {outPath} ({width}×{height})");
            saved++;
        }

        output.WriteLine($"batch: {saved} packed, {skipped.Count} skipped");
        foreach (var name in skipped) output.WriteLine($"skipped: {name} (no recognizable maps)");
        return 0;
    }

}
