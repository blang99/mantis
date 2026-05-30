using System.Reflection;

namespace Mantis.Plugin.UI;

/// <summary>
/// On first launch, extracts the embedded brand fonts (Orbitron, Space Grotesk,
/// JetBrains Mono) and copies them to the user's font directory so the UI
/// renders with the exact same typography as the MANTIS marketing site.
///
/// macOS:   ~/Library/Fonts/  (auto-loaded by the system at next app launch)
/// Windows: %LOCALAPPDATA%/Microsoft/Windows/Fonts/  (auto-loaded)
/// </summary>
public static class FontInstaller
{
    private static readonly string[] FontResourceNames =
    {
        "Orbitron.ttf",
        "SpaceGrotesk.ttf",
        "JetBrainsMono.ttf"
    };

    /// <summary>
    /// Idempotent — only writes fonts that aren't already in the user font dir.
    /// Returns the number of new fonts installed.
    /// </summary>
    public static int EnsureInstalled()
    {
        try
        {
            var targetDir = GetUserFontDir();
            if (string.IsNullOrEmpty(targetDir)) return 0;
            Directory.CreateDirectory(targetDir);

            var assembly = Assembly.GetExecutingAssembly();
            var resourceNames = assembly.GetManifestResourceNames();
            int installed = 0;

            foreach (var fontFile in FontResourceNames)
            {
                var targetPath = Path.Combine(targetDir, fontFile);
                if (File.Exists(targetPath)) continue;

                var resourceName = resourceNames.FirstOrDefault(n => n.EndsWith(fontFile, StringComparison.OrdinalIgnoreCase));
                if (resourceName == null) continue;

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) continue;

                using var outFile = File.Create(targetPath);
                stream.CopyTo(outFile);
                installed++;
            }

            return installed;
        }
        catch
        {
            // Font installation is best-effort — if it fails, the UI falls
            // back to the system default sans-serif and still works.
            return 0;
        }
    }

    private static string GetUserFontDir()
    {
        // macOS — ~/Library/Fonts/
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, "Library", "Fonts");
        }

        // Windows — %LOCALAPPDATA%/Microsoft/Windows/Fonts/
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "Microsoft", "Windows", "Fonts");
        }

        return "";
    }
}
