using System.Text.Json;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Mantis.Plugin.Explain;

public class ComponentAnalyzer
{
    public string AnalyzeComponent(IGH_DocumentObject obj)
    {
        if (obj is GH_Component comp)
            return AnalyzeGhComponent(comp);
        if (obj is GH_NumberSlider slider)
            return AnalyzeSlider(slider);
        if (obj is GH_Panel panel)
            return AnalyzePanel(panel);
        if (obj is GH_BooleanToggle toggle)
            return AnalyzeToggle(toggle);

        return JsonSerializer.Serialize(new
        {
            type = "unknown",
            name = obj.Name,
            nickName = obj.NickName
        });
    }

    public string AnalyzeChain(List<IGH_DocumentObject> chain)
    {
        var components = chain.Select(obj => JsonSerializer.Deserialize<object>(AnalyzeComponent(obj))).ToList();
        return JsonSerializer.Serialize(new { chain = components },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static string AnalyzeGhComponent(GH_Component comp)
    {
        var inputs = comp.Params.Input.Select((p, i) =>
        {
            var sources = p.Sources.Select(s =>
            {
                var owner = s.Attributes?.GetTopLevel?.DocObject;
                return new
                {
                    from = owner?.Name ?? "unknown",
                    outputName = s.Name,
                    type = s.TypeName
                };
            }).ToList();

            return new
            {
                index = i,
                name = p.Name,
                type = p.TypeName,
                connected = p.SourceCount > 0,
                sourceCount = p.SourceCount,
                hasData = p.VolatileDataCount > 0,
                sources
            };
        }).ToList();

        var outputs = comp.Params.Output.Select((p, i) => new
        {
            index = i,
            name = p.Name,
            type = p.TypeName,
            recipientCount = p.Recipients.Count,
            hasData = p.VolatileDataCount > 0
        }).ToList();

        return JsonSerializer.Serialize(new
        {
            type = "component",
            name = comp.Name,
            nickName = comp.NickName,
            category = comp.Category,
            subcategory = comp.SubCategory,
            description = comp.Description,
            inputs,
            outputs,
            hasErrors = comp.RuntimeMessageLevel == GH_RuntimeMessageLevel.Error,
            hasWarnings = comp.RuntimeMessageLevel == GH_RuntimeMessageLevel.Warning
        });
    }

    private static string AnalyzeSlider(GH_NumberSlider slider)
    {
        return JsonSerializer.Serialize(new
        {
            type = "slider",
            name = "Number Slider",
            nickName = slider.NickName,
            value = (double)slider.Slider.Value,
            min = (double)slider.Slider.Minimum,
            max = (double)slider.Slider.Maximum,
            decimals = slider.Slider.DecimalPlaces,
            recipientCount = slider.Recipients.Count
        });
    }

    private static string AnalyzePanel(GH_Panel panel)
    {
        return JsonSerializer.Serialize(new
        {
            type = "panel",
            name = "Panel",
            text = panel.UserText,
            sourceCount = panel.Sources.Count
        });
    }

    private static string AnalyzeToggle(GH_BooleanToggle toggle)
    {
        return JsonSerializer.Serialize(new
        {
            type = "toggle",
            name = "Boolean Toggle",
            nickName = toggle.NickName,
            value = toggle.Value,
            recipientCount = toggle.Recipients.Count
        });
    }
}
