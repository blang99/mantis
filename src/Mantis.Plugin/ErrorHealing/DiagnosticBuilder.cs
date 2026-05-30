using System.Text.Json;
using Grasshopper.Kernel;

namespace Mantis.Plugin.ErrorHealing;

public class DiagnosticBuilder
{
    public string BuildDiagnosticContext(List<ComponentError> errors, GH_Document document)
    {
        var diagnostics = errors.Select(error => new
        {
            component = error.ComponentName,
            nickName = error.NickName,
            errors = error.Messages,
            inputs = error.InputInfo.Select(i => new
            {
                i.Index,
                i.Name,
                type = i.TypeName,
                connected = i.SourceCount > 0,
                i.HasData
            }),
            outputs = error.OutputInfo.Select(o => new
            {
                o.Index,
                o.Name,
                type = o.TypeName,
                recipientCount = o.SourceCount
            }),
            upstream = GetUpstreamContext(error.InstanceGuid, document),
            downstream = GetDownstreamContext(error.InstanceGuid, document)
        }).ToList();

        return JsonSerializer.Serialize(new { errorComponents = diagnostics },
            new JsonSerializerOptions { WriteIndented = true });
    }

    private static List<object> GetUpstreamContext(Guid instanceGuid, GH_Document document)
    {
        var result = new List<object>();
        var comp = document.FindObject(instanceGuid, true) as GH_Component;
        if (comp == null) return result;

        foreach (var input in comp.Params.Input)
        {
            foreach (var source in input.Sources)
            {
                var owner = source.Attributes?.GetTopLevel?.DocObject as GH_Component;
                if (owner == null) continue;

                result.Add(new
                {
                    name = owner.Name,
                    hasErrors = owner.RuntimeMessageLevel == GH_RuntimeMessageLevel.Error,
                    outputIndex = owner.Params.Output.IndexOf(source as IGH_Param ?? owner.Params.Output[0]),
                    outputType = source.TypeName
                });
            }
        }

        return result;
    }

    private static List<object> GetDownstreamContext(Guid instanceGuid, GH_Document document)
    {
        var result = new List<object>();
        var comp = document.FindObject(instanceGuid, true) as GH_Component;
        if (comp == null) return result;

        foreach (var output in comp.Params.Output)
        {
            foreach (var recipient in output.Recipients)
            {
                var owner = recipient.Attributes?.GetTopLevel?.DocObject as GH_Component;
                if (owner == null) continue;

                result.Add(new
                {
                    name = owner.Name,
                    inputIndex = owner.Params.Input.IndexOf(recipient as IGH_Param ?? owner.Params.Input[0]),
                    inputType = recipient.TypeName
                });
            }
        }

        return result;
    }
}
