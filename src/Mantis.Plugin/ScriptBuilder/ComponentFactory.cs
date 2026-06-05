using System.Text.Json;
using Grasshopper;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Mantis.Plugin.Knowledge;

namespace Mantis.Plugin.ScriptBuilder;

public class ComponentFactory
{
    private readonly ComponentRegistry _registry;

    public ComponentFactory(ComponentRegistry registry)
    {
        _registry = registry;
    }

    public IGH_DocumentObject? CreateComponent(ComponentDef def)
    {
        // Handle special components FIRST — they aren't in the registry
        if (IsNumberSlider(def.Name))
            return CreateNumberSlider(def);
        if (IsPanel(def.Name))
            return CreatePanel(def);
        if (IsBooleanToggle(def.Name))
            return CreateBooleanToggle(def);

        var info = _registry.FindByName(def.Name);
        if (info == null) return null;

        var obj = Instances.ComponentServer.EmitObject(info.Guid);

        // Stale/placeholder GUID recovery: a few curated catalog entries ship with a
        // placeholder GUID (e.g. "Box", whose real component is named "Box 2Pt" with
        // nickname "Box"). The name resolves, but EmitObject(placeholderGuid) returns
        // null, silently dropping the component. Fall back to locating the real proxy
        // by Name/NickName so the component is still placed.
        if (obj == null)
            obj = EmitByName(def.Name, info.Name, info.NickName);

        if (obj == null) return null;

        obj.CreateAttributes();
        SetInputValues(obj as GH_Component, def);
        return obj;
    }

    /// <summary>
    /// Emit a component by matching the live ComponentServer proxies on Name (preferred)
    /// or NickName. GUID-agnostic, so it recovers from a stale/placeholder catalog GUID.
    /// Skips obsolete proxies so we never resurrect a deprecated variant.
    /// </summary>
    private static IGH_DocumentObject? EmitByName(params string?[] names)
    {
        var wanted = names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!.Trim())
            .ToList();
        if (wanted.Count == 0) return null;

        IGH_ObjectProxy? nickMatch = null;
        foreach (var proxy in Instances.ComponentServer.ObjectProxies)
        {
            if (proxy?.Desc == null || proxy.Obsolete) continue;

            if (wanted.Any(w => string.Equals(proxy.Desc.Name, w, StringComparison.OrdinalIgnoreCase)))
                return Instances.ComponentServer.EmitObject(proxy.Guid); // exact Name wins

            if (nickMatch == null &&
                wanted.Any(w => string.Equals(proxy.Desc.NickName, w, StringComparison.OrdinalIgnoreCase)))
                nickMatch = proxy;
        }

        return nickMatch != null ? Instances.ComponentServer.EmitObject(nickMatch.Guid) : null;
    }

    /// <summary>
    /// True if a component with this name can actually be placed — either a
    /// special object (slider/panel/toggle) or a name the registry resolves to a
    /// real GUID. Used by the validation pass to detect hallucinated names BEFORE
    /// building, so the model can be asked to correct them.
    /// </summary>
    public bool CanResolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        if (IsNumberSlider(name) || IsPanel(name) || IsBooleanToggle(name)) return true;
        return _registry.FindByName(name) != null;
    }

    private static bool IsNumberSlider(string name) =>
        name.Equals("Number Slider", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Slider", StringComparison.OrdinalIgnoreCase);

    private static bool IsPanel(string name) =>
        name.Equals("Panel", StringComparison.OrdinalIgnoreCase);

    private static bool IsBooleanToggle(string name) =>
        name.Equals("Boolean Toggle", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Toggle", StringComparison.OrdinalIgnoreCase);

    private static GH_NumberSlider CreateNumberSlider(ComponentDef def)
    {
        var slider = new GH_NumberSlider();
        slider.CreateAttributes();

        if (def.InputValues?.TryGetValue("0", out var config) == true)
        {
            if (config is JsonElement je && je.ValueKind == JsonValueKind.Object)
            {
                var min = je.TryGetProperty("min", out var minProp) ? minProp.GetDecimal() : 0m;
                var max = je.TryGetProperty("max", out var maxProp) ? maxProp.GetDecimal() : 10m;
                var val = je.TryGetProperty("value", out var valProp) ? valProp.GetDecimal() : 5m;
                var dec = je.TryGetProperty("decimals", out var decProp) ? decProp.GetInt32() : 1;

                slider.Slider.Minimum = min;
                slider.Slider.Maximum = max;
                slider.Slider.Value = val;
                slider.Slider.DecimalPlaces = dec;
                slider.Slider.Type = dec == 0
                    ? Grasshopper.GUI.Base.GH_SliderAccuracy.Even
                    : Grasshopper.GUI.Base.GH_SliderAccuracy.Float;
            }
        }

        if (!string.IsNullOrEmpty(def.NickName))
            slider.NickName = def.NickName;

        return slider;
    }

    private static GH_Panel CreatePanel(ComponentDef def)
    {
        var panel = new GH_Panel();
        panel.CreateAttributes();

        if (def.InputValues?.TryGetValue("0", out var text) == true)
        {
            var value = text is JsonElement je ? je.GetString() ?? "" : text?.ToString() ?? "";
            panel.SetUserText(value);
        }

        return panel;
    }

    private static GH_BooleanToggle CreateBooleanToggle(ComponentDef def)
    {
        var toggle = new GH_BooleanToggle();
        toggle.CreateAttributes();

        if (def.InputValues?.TryGetValue("0", out var val) == true)
        {
            bool value = val is JsonElement je ? je.GetBoolean() : Convert.ToBoolean(val);
            toggle.Value = value;
        }

        if (!string.IsNullOrEmpty(def.NickName))
            toggle.NickName = def.NickName;

        return toggle;
    }

    /// <summary>
    /// Apply inline input values (a number/string/bool the model set directly on
    /// an input instead of wiring a slider). These MUST be written as PERSISTENT
    /// data, not volatile — Grasshopper clears volatile data at the start of every
    /// solution, so a volatile value would silently vanish on the first recompute,
    /// breaking the graph. Persistent data is the programmatic equivalent of
    /// right-click → "Set value" and survives every solution.
    /// </summary>
    private static void SetInputValues(GH_Component? comp, ComponentDef def)
    {
        if (comp == null || def.InputValues == null) return;

        var path = new GH_Path(0);

        foreach (var (key, value) in def.InputValues)
        {
            if (!int.TryParse(key, out var paramIndex)) continue;
            if (paramIndex < 0 || paramIndex >= comp.Params.Input.Count) continue;
            if (value is not JsonElement je) continue;

            var param = comp.Params.Input[paramIndex];

            try
            {
                switch (param)
                {
                    case Param_Integer pi when je.ValueKind == JsonValueKind.Number:
                        pi.PersistentData.Clear();
                        pi.PersistentData.Append(new GH_Integer((int)Math.Round(je.GetDouble())), path);
                        pi.ExpireSolution(false);
                        break;

                    case Param_Number pn when je.ValueKind == JsonValueKind.Number:
                        pn.PersistentData.Clear();
                        pn.PersistentData.Append(new GH_Number(je.GetDouble()), path);
                        pn.ExpireSolution(false);
                        break;

                    case Param_Boolean pb when je.ValueKind is JsonValueKind.True or JsonValueKind.False:
                        pb.PersistentData.Clear();
                        pb.PersistentData.Append(new GH_Boolean(je.GetBoolean()), path);
                        pb.ExpireSolution(false);
                        break;

                    case Param_String ps when je.ValueKind == JsonValueKind.String:
                        ps.PersistentData.Clear();
                        ps.PersistentData.Append(new GH_String(je.GetString() ?? ""), path);
                        ps.ExpireSolution(false);
                        break;

                    // A non-scalar param (generic/typed object) received an inline value.
                    // We deliberately do NOT call AddVolatileData here: this method runs
                    // inside a scheduled solution, where adding volatile data throws
                    // ("cannot modify data during a solution") AND any volatile value is
                    // wiped on the very next recompute — so it was never persistent anyway.
                    // The prompt steers the model to wire a slider/source for these cases,
                    // which survives every solution. Skipping is the safe, error-free path.
                    default:
                        break;
                }
            }
            catch
            {
                // Skip values that can't be set
            }
        }
    }
}
