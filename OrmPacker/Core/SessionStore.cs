using System.Text.Json;

namespace OrmPacker;

/// <summary>State of one session slot. The path may be absent, in which case the slot is a constant.</summary>
public sealed record SlotState(string? Path, SourceComponent Source, byte SolidValue, bool Invert);

/// <summary>Data persisted between runs. Local JSON only, no network.</summary>
public sealed class SessionState
{
    public string PresetId { get; set; } = "orm";
    public int? SizePixels { get; set; } = 1024;
    public string FormatId { get; set; } = "png";
    public bool ConvertMode { get; set; }
    public string? FromPresetId { get; set; }
    public string? ConvertSourcePath { get; set; }
    public SlotState[] Slots { get; set; } = Enumerable.Range(0, 4)
        .Select(_ => new SlotState(null, SourceComponent.Red, 255, false)).ToArray();
    public List<string> RecentPaths { get; set; } = new();
    public string? LastOutputDirectory { get; set; }
}

public static class SessionStore
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "OrmPacker", "session.json");

    public static SessionState Load(string path)
    {
        if (!File.Exists(path)) return new SessionState();
        try
        {
            var session = JsonSerializer.Deserialize<SessionState>(File.ReadAllText(path)) ?? new SessionState();
            Normalize(session);
            return session;
        }
        catch (JsonException) { return new SessionState(); }
        catch (IOException) { return new SessionState(); }
        catch (UnauthorizedAccessException) { return new SessionState(); }
    }

    /// <summary>A hand-edited session must not be able to crash the form on startup.</summary>
    private static void Normalize(SessionState session)
    {
        session.Slots ??= Array.Empty<SlotState>();
        session.RecentPaths ??= new List<string>();
        for (var i = 0; i < session.Slots.Length; i++)
        {
            var slot = session.Slots[i];
            if (slot is null)
            {
                session.Slots[i] = new SlotState(null, SourceComponent.Red, 255, false);
                continue;
            }
            if (!Enum.IsDefined(slot.Source))
                session.Slots[i] = slot with { Source = SourceComponent.Red };
        }
    }

    public static void Save(string path, SessionState state)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        var temp = path + ".tmp";
        // Serialize to a temp file first, then replace: a failed write cannot truncate the last good session.
        File.WriteAllText(temp, JsonSerializer.Serialize(state));
        File.Move(temp, path, overwrite: true);
    }

    public static string PresetId(Preset preset)
    {
        if (ReferenceEquals(preset, Preset.Orm)) return "orm";
        if (ReferenceEquals(preset, Preset.UnityHdrp)) return "hdrp";
        if (ReferenceEquals(preset, Preset.UnityUrp)) return "urp";
        return "custom";
    }

    public static Preset PresetById(string? id)
    {
        if (id == "hdrp") return Preset.UnityHdrp;
        if (id == "urp") return Preset.UnityUrp;
        if (id == "custom") return Preset.Custom;
        return Preset.Orm;
    }

    public static string FormatId(SaveFormat format) => format.ToString().ToLowerInvariant();

    public static SaveFormat FormatById(string? id) => Enum.TryParse<SaveFormat>(id, ignoreCase: true,
        out var format) ? format : SaveFormat.Png;
}
