using System.Drawing;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Mantis.Plugin.ScriptBuilder;

/// <summary>
/// Turns a script's logical stages (<see cref="GroupDef"/>) into real
/// <see cref="GH_Group"/> boxes on the Grasshopper canvas. Each stage becomes a
/// labelled, tinted group so the generated workflow reads top-to-bottom the way
/// a human expert would annotate it. Returns the GUIDs of the groups it created
/// so the caller can track them for clean iteration teardown.
/// </summary>
internal static class CanvasGrouping
{
    // Soft, low-alpha tints so the group fill never overpowers the components.
    private static readonly Color[] Palette =
    {
        Color.FromArgb(60, 92, 219, 122),   // mantis green
        Color.FromArgb(60, 90, 170, 255),   // sky blue
        Color.FromArgb(60, 255, 184, 77),   // amber
        Color.FromArgb(60, 196, 128, 255),  // violet
        Color.FromArgb(60, 255, 110, 132),  // coral
        Color.FromArgb(60, 80, 214, 220),   // cyan
    };

    /// <summary>
    /// Creates one GH_Group per stage. Members are resolved from the live
    /// placement map (component id -> object). Empty or unresolved stages are
    /// skipped silently. Safe to call when <paramref name="script"/> has no
    /// groups (older models / simple scripts) — it simply does nothing.
    /// </summary>
    public static List<Guid> Apply(
        GH_Document doc,
        ScriptDefinition script,
        IReadOnlyDictionary<int, IGH_DocumentObject> placed)
    {
        var created = new List<Guid>();
        if (doc == null || script?.Groups == null || script.Groups.Count == 0)
            return created;

        int index = 0;
        foreach (var g in script.Groups)
        {
            if (g?.ComponentIds == null || g.ComponentIds.Count == 0)
                continue;

            var memberGuids = new List<Guid>();
            foreach (var id in g.ComponentIds)
                if (placed.TryGetValue(id, out var obj) && obj != null)
                    memberGuids.Add(obj.InstanceGuid);

            if (memberGuids.Count == 0)
                continue;

            try
            {
                var group = new GH_Group { NickName = g.Name ?? string.Empty };
                group.CreateAttributes();
                group.Colour = ResolveColour(g.Color, index);

                // Members must be added BEFORE the group is added to the doc so
                // its bounds compute correctly.
                foreach (var guid in memberGuids)
                    group.AddObject(guid);

                doc.AddObject(group, false);
                group.ExpireCaches();

                created.Add(group.InstanceGuid);
                index++;
            }
            catch
            {
                // A single malformed stage must never abort the whole build.
            }
        }

        return created;
    }

    private static Color ResolveColour(string? hex, int index)
    {
        if (!string.IsNullOrWhiteSpace(hex) && TryParseHex(hex!, out var c))
            return Color.FromArgb(60, c.R, c.G, c.B);
        return Palette[index % Palette.Length];
    }

    private static bool TryParseHex(string hex, out Color color)
    {
        color = Color.Empty;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6)
            return false;
        try
        {
            color = Color.FromArgb(
                Convert.ToInt32(hex.Substring(0, 2), 16),
                Convert.ToInt32(hex.Substring(2, 2), 16),
                Convert.ToInt32(hex.Substring(4, 2), 16));
            return true;
        }
        catch
        {
            return false;
        }
    }
}
