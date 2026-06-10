using System;
using System.Collections.Generic;
using System.IO;
using Mantis.Plugin.Eval;
using Mantis.Plugin.UI;
using Rhino;
using Rhino.Commands;

namespace Mantis.Plugin;

/// <summary>
/// The <c>MantisEval</c> command — MANTIS's quality system. Runs the frozen eval corpus through the
/// REAL generation + validation pipeline with the user's configured provider and writes a per-tier
/// scorecard to %AppData%/Mantis/eval-&lt;timestamp&gt;.md. This converts "does MANTIS work?" from
/// opinion into numbers (the #1 recommendation from the product review).
///
/// Structural dimensions (parsed / validated-clean / names-resolved / trait-coverage) run here; the
/// live solve+geometry dimensions are added in a follow-up. NOTE: this calls your LLM provider once
/// per case — for a cloud key that is ~30 calls of cost/latency.
/// </summary>
public class MantisEvalCommand : Command
{
    public override string EnglishName => "MantisEval";

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var corpus = EvalCorpus.Load();
        if (corpus.Count == 0)
        {
            RhinoApp.WriteLine("MANTIS eval: no corpus embedded.");
            return Result.Failure;
        }

        var service = MantisPanelHost.GetOrCreateService();
        RhinoApp.WriteLine($"MANTIS eval: running {corpus.Count} cases via provider '{service.ActiveProviderName}' "
                           + $"— this calls your LLM {corpus.Count} times, please wait…");

        var scores = new List<EvalScore>();
        foreach (var c in corpus)
        {
            EvalScore s;
            try
            {
                var script = service.GenerateScriptOnlyAsync(c.Prompt).GetAwaiter().GetResult();
                s = service.ScoreStructural(c, script);
            }
            catch (Exception ex)
            {
                RhinoApp.WriteLine($"  {c.Id}: ERROR {ex.Message}");
                s = new EvalScore { Id = c.Id, Tier = c.Tier };
            }
            scores.Add(s);
            RhinoApp.WriteLine($"  [{s.Tier,-11}] {c.Id}: struct={s.StructuralScore:0.00} "
                               + $"parsed={(s.Parsed ? 1 : 0)} clean={(s.ValidatedClean ? 1 : 0)} "
                               + $"resolved={(s.AllNamesResolved ? 1 : 0)} traits={s.TraitCoverage:0.0}");
        }

        var stamp = DateTime.Now.ToString("yyyy-MM-dd_HHmm");
        var report = EvalReport.Render(scores, service.ActiveProviderName, stamp);

        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mantis");
        string path = Path.Combine(dir, $"eval-{stamp}.md");
        try { Directory.CreateDirectory(dir); File.WriteAllText(path, report); }
        catch (Exception ex) { RhinoApp.WriteLine($"  (could not write report: {ex.Message})"); path = "(not written)"; }

        var sum = EvalScorer.Summarize(scores);
        RhinoApp.WriteLine($"MANTIS eval complete — parsed {sum.Parsed}/{sum.Total}, "
                           + $"clean {sum.ValidatedClean}/{sum.Total}, resolved {sum.AllNamesResolved}/{sum.Total}, "
                           + $"mean structural {sum.MeanStructuralScore:0.000}. Report: {path}");
        return Result.Success;
    }
}
