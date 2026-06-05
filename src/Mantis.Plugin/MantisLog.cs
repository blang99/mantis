using System;
using System.IO;

namespace Mantis.Plugin;

/// <summary>
/// Best-effort diagnostic logging. Writes to the Rhino command line AND to
/// <c>&lt;AppData&gt;/Mantis/mantis-log.txt</c> so failures on platforms we can't
/// directly test (e.g. Windows) can be retrieved and sent back. Never throws.
/// </summary>
internal static class MantisLog
{
    private static string LogPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mantis");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "mantis-log.txt");
        }
    }

    public static void Info(string message)
    {
        Write("INFO ", message);
    }

    public static void Error(string context, Exception ex)
    {
        Write("ERROR", context + " — " + ex);
    }

    private static void Write(string level, string text)
    {
        var line = "MANTIS " + level + ": " + text;
        try { Rhino.RhinoApp.WriteLine(line); } catch { /* command line not ready */ }
        try { File.AppendAllText(LogPath, DateTime.Now.ToString("u") + "  " + line + Environment.NewLine + Environment.NewLine); }
        catch { /* disk not writable — command line still has it */ }
    }
}
