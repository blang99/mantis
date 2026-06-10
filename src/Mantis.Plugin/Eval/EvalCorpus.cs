using System.Reflection;
using System.Text.Json;

namespace Mantis.Plugin.Eval;

/// <summary>Loads the embedded frozen eval corpus (EvalPrompts.json). Never throws.</summary>
public static class EvalCorpus
{
    public static List<EvalCase> Load()
    {
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("EvalPrompts.json"));
            if (name == null) return new();
            using var s = asm.GetManifestResourceStream(name);
            if (s == null) return new();
            return JsonSerializer.Deserialize<List<EvalCase>>(s,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
        }
        catch { return new(); }
    }
}
