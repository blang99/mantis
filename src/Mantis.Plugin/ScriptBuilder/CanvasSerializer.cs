using System.Text.Json;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Mantis.Plugin.ScriptBuilder;

public class CanvasSerializer
{
    public string Serialize(GH_Document document)
    {
        var components = new List<object>();
        var connections = new List<object>();
        var componentMap = new Dictionary<Guid, int>();
        int nextId = 1;

        foreach (var obj in document.Objects)
        {
            if (obj is GH_Component comp)
            {
                var id = nextId++;
                componentMap[comp.InstanceGuid] = id;

                var inputs = comp.Params.Input.Select((p, i) => new
                {
                    index = i,
                    name = p.Name,
                    nickName = p.NickName,
                    type = p.TypeName,
                    hasData = p.SourceCount > 0 || p.VolatileDataCount > 0
                }).ToList();

                var outputs = comp.Params.Output.Select((p, i) => new
                {
                    index = i,
                    name = p.Name,
                    nickName = p.NickName,
                    type = p.TypeName
                }).ToList();

                components.Add(new
                {
                    id,
                    name = comp.Name,
                    nickName = comp.NickName,
                    category = comp.Category,
                    x = comp.Attributes.Pivot.X,
                    y = comp.Attributes.Pivot.Y,
                    inputs,
                    outputs,
                    hasErrors = comp.RuntimeMessageLevel == GH_RuntimeMessageLevel.Error,
                    hasWarnings = comp.RuntimeMessageLevel == GH_RuntimeMessageLevel.Warning
                });
            }
            else if (obj is GH_NumberSlider slider)
            {
                var id = nextId++;
                componentMap[slider.InstanceGuid] = id;
                components.Add(new
                {
                    id,
                    name = "Number Slider",
                    nickName = slider.NickName,
                    value = (double)slider.Slider.Value,
                    min = (double)slider.Slider.Minimum,
                    max = (double)slider.Slider.Maximum,
                    decimals = slider.Slider.DecimalPlaces
                });
            }
            else if (obj is GH_Panel panel)
            {
                var id = nextId++;
                componentMap[panel.InstanceGuid] = id;
                components.Add(new
                {
                    id,
                    name = "Panel",
                    text = panel.UserText
                });
            }
            else if (obj is GH_BooleanToggle toggle)
            {
                var id = nextId++;
                componentMap[toggle.InstanceGuid] = id;
                components.Add(new
                {
                    id,
                    name = "Boolean Toggle",
                    nickName = toggle.NickName,
                    value = toggle.Value
                });
            }
        }

        // Serialize connections
        foreach (var obj in document.Objects)
        {
            if (obj is not GH_Component comp) continue;
            if (!componentMap.TryGetValue(comp.InstanceGuid, out var toId)) continue;

            for (int i = 0; i < comp.Params.Input.Count; i++)
            {
                foreach (var source in comp.Params.Input[i].Sources)
                {
                    var sourceOwner = source.Attributes?.GetTopLevel?.DocObject;
                    if (sourceOwner == null) continue;
                    if (!componentMap.TryGetValue(sourceOwner.InstanceGuid, out var fromId)) continue;

                    int fromOutput = 0;
                    if (sourceOwner is GH_Component sourceComp)
                        fromOutput = sourceComp.Params.Output.IndexOf(source as IGH_Param ?? sourceComp.Params.Output[0]);

                    connections.Add(new
                    {
                        fromComponent = fromId,
                        fromOutput,
                        toComponent = toId,
                        toInput = i
                    });
                }
            }
        }

        var canvasState = new { components, connections };
        return JsonSerializer.Serialize(canvasState, new JsonSerializerOptions { WriteIndented = true });
    }

    public string SerializeErrors(GH_Document document)
    {
        var errors = new List<object>();

        foreach (var obj in document.Objects)
        {
            if (obj is not GH_Component comp) continue;
            if (comp.RuntimeMessageLevel != GH_RuntimeMessageLevel.Error) continue;

            var messages = new List<string>();
            foreach (var msg in comp.RuntimeMessages(GH_RuntimeMessageLevel.Error))
                messages.Add(msg);

            var inputSources = comp.Params.Input.Select((p, i) => new
            {
                index = i,
                name = p.Name,
                type = p.TypeName,
                sourceCount = p.SourceCount,
                hasData = p.VolatileDataCount > 0
            }).ToList();

            errors.Add(new
            {
                name = comp.Name,
                nickName = comp.NickName,
                guid = comp.InstanceGuid.ToString(),
                messages,
                inputs = inputSources
            });
        }

        return JsonSerializer.Serialize(errors, new JsonSerializerOptions { WriteIndented = true });
    }
}
