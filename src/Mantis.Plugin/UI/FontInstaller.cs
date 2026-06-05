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
    // Embedded resource file → the font's installed family (display) name. The
    // family name is what the UI asks Eto for (e.g. new Font("Space Grotesk", …)),
    // so on Windows the registry entry must be keyed to exactly these names.
    private static readonly (string File, string Family)[] Fonts =
    {
        ("Orbitron.ttf",      "Orbitron"),
        ("SpaceGrotesk.ttf",  "Space Grotesk"),
        ("JetBrainsMono.ttf", "JetBrains Mono"),
    };

    // Register-on-Windows is done at most once per Rhino session.
    private static bool _winRegistered;

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
            bool onWindows = OperatingSystem.IsWindows();

            foreach (var (fontFile, family) in Fonts)
            {
                var targetPath = Path.Combine(targetDir, fontFile);

                if (!File.Exists(targetPath))
                {
                    var resourceName = resourceNames.FirstOrDefault(n => n.EndsWith(fontFile, StringComparison.OrdinalIgnoreCase));
                    if (resourceName == null) continue;

                    using var stream = assembly.GetManifestResourceStream(resourceName);
                    if (stream == null) continue;

                    using var outFile = File.Create(targetPath);
                    stream.CopyTo(outFile);
                    installed++;
                }

                // On Windows, dropping the .ttf into the per-user fonts folder is NOT
                // enough — the font also needs a per-user registry entry, otherwise
                // WPF/DirectWrite (the toolkit behind Rhino's Eto UI on Windows) can't
                // resolve it by name and the UI falls back to a default font. macOS
                // auto-discovers fonts in ~/Library/Fonts, so no extra step there.
                if (onWindows && !_winRegistered)
                    RegisterWindowsFont(targetPath, family);
            }

            if (onWindows) _winRegistered = true;
            return installed;
        }
        catch
        {
            // Font installation is best-effort — if it fails, the UI falls
            // back to the system default sans-serif and still works.
            return 0;
        }
    }

    /// <summary>
    /// Per-user font registration on Windows (no admin rights needed): writes the
    /// HKCU font registry value so the family resolves on the next Rhino launch.
    /// Uses the built-in <c>reg</c> tool to avoid taking a registry-package
    /// dependency. Best-effort — a failure just means a system-font fallback.
    /// </summary>
    private static void RegisterWindowsFont(string fontPath, string family)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "reg",
                Arguments = "add \"HKCU\\Software\\Microsoft\\Windows NT\\CurrentVersion\\Fonts\" " +
                            $"/v \"{family} (TrueType)\" /t REG_SZ /d \"{fontPath}\" /f",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
            };
            System.Diagnostics.Process.Start(psi)?.WaitForExit(3000);
        }
        catch { /* best-effort */ }
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
