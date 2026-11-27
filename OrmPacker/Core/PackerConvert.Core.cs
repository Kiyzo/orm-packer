namespace OrmPacker;

/// <summary>
/// Convert mode: an already packed map is repacked into another preset without losing
/// channel semantics. Uses the same core as regular packing (no duplicated logic in GUI/CLI).
/// </summary>
public static partial class PackerConvert
{
    /// <summary>A single mapping entry: target channel ← source channel (or a constant).</summary>
    public sealed record ConvertMapping(Channel Target, Channel? Source, bool Invert)
    {
        public bool IsConstant => Source is null;
    }

    /// <summary>
    /// Single source of truth for channel layout during conversion. Used by both core and UI —
    /// the mapping panel does not duplicate channel-selection or inversion logic.
    /// </summary>
    public static IReadOnlyList<ConvertMapping> BuildMappings(Preset from, Preset to)
    {
        var mappings = new List<ConvertMapping>(4);
        foreach (var targetChannel in new[] { Channel.R, Channel.G, Channel.B, Channel.A })
        {
            var target = to.Channels[targetChannel];
            if (!target.HasMap)
            {
                mappings.Add(new ConvertMapping(targetChannel, null, false));
                continue;
            }

            var sourceChannel = FindChannel(from, kind => kind == target.Kind);
            var invert = false;
            if (sourceChannel is null)
            {
                sourceChannel = FindChannel(from, kind => Preset.IsInvertedPair(kind, target.Kind));
                invert = sourceChannel is not null;
            }
            // Custom has no named maps: fall back to the same channel index (R→R, G→G, …).
            if (sourceChannel is null && from == Preset.Custom) sourceChannel = targetChannel;

            mappings.Add(new ConvertMapping(targetChannel, sourceChannel, invert));
        }
        return mappings;
    }

    /// <summary>Output channels for the target preset. With no source pair, the default constant is used.</summary>
    public static ChannelInput[] BuildInputs(TextureImage source, Preset from, Preset to)
    {
        var inputs = new ChannelInput[4];
        foreach (var mapping in BuildMappings(from, to))
        {
            var target = to.Channels[mapping.Target];
            inputs[(int)mapping.Target] = mapping.Source is null
                ? new ChannelInput(null, SourceComponent.Red, target.DefaultValue, false,
                    Preset.FilterFor(target.Kind))
                : new ChannelInput(source, (SourceComponent)mapping.Source.Value, target.DefaultValue,
                    mapping.Invert, Preset.FilterFor(target.Kind));
        }
        return inputs;
    }

    private static Channel? FindChannel(Preset preset, Func<MapKind, bool> predicate)
    {
        foreach ((var channel, var def) in preset.Channels)
            if (def.HasMap && predicate(def.Kind)) return channel;
        return null;
    }

    public static bool NeedsAlpha(Preset to, IReadOnlyList<ChannelInput> inputs)
    {
        if (to.AlphaMode == AlphaMode.Required) return true;
        if (to.AlphaMode == AlphaMode.None) return false;
        return inputs[(int)Channel.A].Image is not null;
    }

    public static PackedTexture Convert(TextureImage source, Preset from, Preset to,
        OutputSize? size = null) =>
        Convert(source, from, to, size, BuildInputs(source, from, to), out _);

    /// <summary>Converts with prebuilt inputs and reports whether alpha is written.</summary>
    public static PackedTexture Convert(TextureImage source, Preset from, Preset to,
        OutputSize? size, IReadOnlyList<ChannelInput> inputs, out bool alpha)
    {
        (var width, var height) = (size ?? OutputSize.Original).Apply(source.Width, source.Height);
        if (width == 0 || height == 0) (width, height) = (source.Width, source.Height);
        alpha = NeedsAlpha(to, inputs);
        return Packer.Pack(width, height, inputs[0], inputs[1], inputs[2], inputs[3], alpha);
    }
}
