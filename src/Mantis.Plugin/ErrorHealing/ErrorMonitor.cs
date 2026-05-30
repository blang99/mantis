using Grasshopper.Kernel;

namespace Mantis.Plugin.ErrorHealing;

public class ErrorMonitor : IDisposable
{
    private GH_Document? _document;
    private bool _monitoring;

    public event Action<List<ComponentError>>? OnErrorsDetected;
    public event Action? OnErrorsCleared;

    public bool IsMonitoring => _monitoring;
    public List<ComponentError> CurrentErrors { get; private set; } = new();

    public void StartMonitoring(GH_Document document)
    {
        StopMonitoring();
        _document = document;
        _document.SolutionEnd += OnSolutionEnd;
        _monitoring = true;
    }

    public void StopMonitoring()
    {
        if (_document != null)
        {
            _document.SolutionEnd -= OnSolutionEnd;
            _document = null;
        }
        _monitoring = false;
        CurrentErrors.Clear();
    }

    private void OnSolutionEnd(object? sender, GH_SolutionEventArgs e)
    {
        if (_document == null) return;

        var errors = new List<ComponentError>();

        foreach (var obj in _document.Objects)
        {
            if (obj is not GH_Component comp) continue;
            if (comp.RuntimeMessageLevel != GH_RuntimeMessageLevel.Error) continue;

            var messages = new List<string>();
            foreach (var msg in comp.RuntimeMessages(GH_RuntimeMessageLevel.Error))
                messages.Add(msg);

            if (messages.Count == 0) continue;

            errors.Add(new ComponentError
            {
                ComponentName = comp.Name,
                NickName = comp.NickName,
                InstanceGuid = comp.InstanceGuid,
                Messages = messages,
                InputInfo = GetInputInfo(comp),
                OutputInfo = GetOutputInfo(comp)
            });
        }

        // Capture whether we *had* errors before overwriting CurrentErrors —
        // otherwise the "cleared" branch can never fire (CurrentErrors would
        // already be the new empty list).
        var hadErrors = CurrentErrors.Count > 0;
        CurrentErrors = errors;

        if (errors.Count > 0)
            OnErrorsDetected?.Invoke(errors);
        else if (hadErrors)
            OnErrorsCleared?.Invoke();
    }

    private static List<ParamErrorInfo> GetInputInfo(GH_Component comp)
    {
        return comp.Params.Input.Select((p, i) => new ParamErrorInfo
        {
            Index = i,
            Name = p.Name,
            TypeName = p.TypeName,
            SourceCount = p.SourceCount,
            HasData = p.VolatileDataCount > 0
        }).ToList();
    }

    private static List<ParamErrorInfo> GetOutputInfo(GH_Component comp)
    {
        return comp.Params.Output.Select((p, i) => new ParamErrorInfo
        {
            Index = i,
            Name = p.Name,
            TypeName = p.TypeName,
            SourceCount = p.Recipients.Count,
            HasData = p.VolatileDataCount > 0
        }).ToList();
    }

    public void Dispose() => StopMonitoring();
}

public class ComponentError
{
    public string ComponentName { get; set; } = "";
    public string NickName { get; set; } = "";
    public Guid InstanceGuid { get; set; }
    public List<string> Messages { get; set; } = new();
    public List<ParamErrorInfo> InputInfo { get; set; } = new();
    public List<ParamErrorInfo> OutputInfo { get; set; } = new();
}

public class ParamErrorInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "";
    public int SourceCount { get; set; }
    public bool HasData { get; set; }
}
