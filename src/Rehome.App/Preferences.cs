using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rehome.Shell;

/// <summary>Rehome's own options. One small file per user; a missing or broken file means defaults.</summary>
sealed record Preferences
{
    /// <summary>"system", "light" or "dark".</summary>
    public string Theme { get; init; } = "system";

    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Rehome", "preferences.json");

    public static Preferences Current { get; private set; } = Load();

    static Preferences Load()
    {
        try
        {
            var prefs = JsonSerializer.Deserialize(File.ReadAllText(FilePath), PrefsJson.Default.Preferences) ?? new();
            return prefs.Valid();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new();
        }
    }

    Preferences Valid() => this with { Theme = Theme is "light" or "dark" ? Theme : "system" };

    public static Preferences Save(Preferences prefs)
    {
        Current = prefs.Valid();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            Files.WriteAtomic(FilePath, JsonSerializer.Serialize(Current, PrefsJson.Default.Preferences));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Applied for this session even if it can't be remembered.
            Log.Write(e);
        }
        return Current;
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(Preferences))]
sealed partial class PrefsJson : JsonSerializerContext;
