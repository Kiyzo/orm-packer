namespace OrmPacker;

/// <summary>A slot warning. It does not block saving — it only informs the artist.</summary>
public enum SlotWarning { Empty, Constant, SizeMismatch, DuplicateSource }

/// <summary>Minimum/maximum/average of the final channel (after inversion).</summary>
public readonly record struct ChannelStats(byte Min, byte Max, double Avg, bool IsConstant);

/// <summary>
/// Slot diagnostics and channel statistics. Pure logic: no WinForms dependency.
/// </summary>
public static class SlotDiagnostics
{
    public static ChannelStats Stats(ChannelInput input)
    {
        var invert = input.Invert;
        if (input.Image is null)
        {
            var solid = invert ? (byte)(255 - input.SolidValue) : input.SolidValue;
            return new ChannelStats(solid, solid, solid, true);
        }

        var image = input.Image;
        byte min = 255, max = 0;
        long sum = 0;
        for (var y = 0; y < image.Height; y++)
        for (var x = 0; x < image.Width; x++)
        {
            var value = image.ComponentAt(x, y, input.Source);
            if (invert) value = (byte)(255 - value);
            min = Math.Min(min, value);
            max = Math.Max(max, value);
            sum += value;
        }

        var count = image.Width * image.Height;
        return new ChannelStats(min, max, (double)sum / count, min == max);
    }

    /// <summary>
    /// Diagnostics stay quiet until the first map is loaded in the session. An empty slot
    /// reports only Empty — never Constant (the solid value is an intentional choice).
    /// </summary>
    public static IReadOnlyList<SlotWarning> Warnings(IReadOnlyList<ChannelInput> inputs, int index,
        ChannelStats? stats = null, bool sessionHasMaps = true)
    {
        var result = new List<SlotWarning>();
        if (!sessionHasMaps) return result;

        var input = inputs[index];
        if (input.Image is null)
        {
            result.Add(SlotWarning.Empty);
            return result;
        }
        if ((stats ?? Stats(input)).IsConstant) result.Add(SlotWarning.Constant);

        var reference = input.Image;
        foreach (var other in inputs)
        {
            var candidate = other.Image;
            if (candidate is not null &&
                (long)candidate.Width * candidate.Height > (long)reference.Width * reference.Height)
                reference = candidate;
        }
        if (input.Image.Width != reference.Width || input.Image.Height != reference.Height)
            result.Add(SlotWarning.SizeMismatch);

        for (var i = 0; i < inputs.Count; i++)
        {
            if (i != index && inputs[i].Image is not null && ReferenceEquals(inputs[i].Image, input.Image))
            {
                result.Add(SlotWarning.DuplicateSource);
                break;
            }
        }
        return result;
    }
}
