using System.Text.Json;

namespace Mantis.Plugin;

/// <summary>
/// Tiny read/write helper over the shared <c>&lt;AppData&gt;/Mantis/settings.json</c>
/// document (the same flat key/value file the model picker and settings dialog use for
/// the active provider and masked key hints). Best-effort and exception-safe — settings
/// are a convenience, never load-bearing, so every operation degrades to a no-op / null
/// rather than throwing into the UI or the generation path.
/// </summary>
public static class MantisSettings
{
    /// <summary>Standing user instructions injected into every system prompt.</summary>
    public const string CustomInstructionsKey = "customInstructions";

    private static string Dir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mantis");

    private static string FilePath => Path.Combine(Dir, "settings.json");

    /// <summary>Read a string value, or null if absent/unreadable.</summary>
    public static string? Get(string key)
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
            return doc.RootElement.TryGetProperty(key, out var v) ? v.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Merge one key into settings.json, preserving every other key already there
    /// (provider, key hints, etc.). Values are stored as strings, matching the existing
    /// writers so the file stays one consistent flat document.
    /// </summary>
    public static void Set(string key, string value)
    {
        try
        {
            Directory.CreateDirectory(Dir);

            var settings = new Dictionary<string, object>();
            if (File.Exists(FilePath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(FilePath));
                foreach (var prop in doc.RootElement.EnumerateObject())
                    settings[prop.Name] = prop.Value.ToString();
            }

            settings[key] = value;

            File.WriteAllText(FilePath,
                JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // Persisting settings is best-effort; never surface a write failure.
        }
    }
}
