using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Mantis.Plugin.Eval;
using Mantis.Plugin.UI;
using Rhino;
using Rhino.Commands;

namespace Mantis.Plugin;

/// <summary>
/// The <c>MantisEval</c> command — MANTIS's quality system. Runs the frozen eval corpus through the
/// REAL generation + validation pipeline with the user's configured provider and writes a per-tier
/// scorecard to %AppData%/Mantis/eval-&lt;timestamp&gt;.md.
///
/// Runs the generation loop on a BACKGROUND thread so Rhino stays responsive (an earlier version ran
/// it synchronously on the UI thread, which deadlocked while Grasshopper loaded). Grasshopper + the
/// catalog are loaded ON the UI thread first; the background loop only does HTTP + pure scoring.
/// Re-running the command cancels an in-flight run.
/// </summary>
public class MantisEvalCommand : Command
{
    public override string EnglishName => "MantisEval";

    private static CancellationTokenSource? _running;

    protected override Result RunCommand(RhinoDoc doc, RunMode mode)
    {
        var corpus = EvalCorpus.Load();
        if (corpus.Count == 0)
        {
            RhinoApp.WriteLine("MANTIS eval: no corpus embedded.");
            return Result.Failure;
        }

        // Cancel any prior run, then start a fresh token.
        _running?.Cancel();
        var cts = new CancellationTokenSource();
        _running = cts;

        var service = MantisPanelHost.GetOrCreateService();

        // Load Grasshopper + scan the catalog ON THE UI THREAD now (returns when done). Doing this
        // from inside a blocked async call is what froze Rhino before; here it loads normally.
        RhinoApp.WriteLine("MANTIS eval: loading Grasshopper + catalog…");
        try { service.EnsureReady(); }
        catch (Exception ex)
        {
            RhinoApp.WriteLine($"MANTIS eval: could not load Grasshopper — {ex.Message}");
            return Result.Failure;
        }

        var provider = service.ActiveProviderName;
        RhinoApp.WriteLine($"MANTIS eval: running {corpus.Count} cases via '{provider}' IN THE BACKGROUND — "
                           + "Rhino stays usable; a report is written when it finishes.");
        RhinoApp.WriteLine("  (Tip: a cloud provider is far faster than local Ollama for 30 calls. Re-run MantisEval to cancel.)");

        _ = Task.Run(async () =>
        {
            var scores = new List<EvalScore>();
            foreach (var c in corpus)
            {
                if (cts.IsCancellationRequested) { RhinoApp.WriteLine("MANTIS eval: cancelled."); return; }
                EvalScore s;
                try
                {
                    var script = await service.GenerateScriptOnlyAsync(c.Prompt, cts.Token);
                    s = service.ScoreStructural(c, script);
                }
                catch (OperationCanceledException) { RhinoApp.WriteLine("MANTIS eval: cancelled."); return; }
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
            var report = EvalReport.Render(scores, provider, stamp);
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mantis");
            string path;
            try { Directory.CreateDirectory(dir); path = Path.Combine(dir, $"eval-{stamp}.md"); File.WriteAllText(path, report); }
            catch (Exception ex) { RhinoApp.WriteLine($"  (could not write report: {ex.Message})"); path = "(not written)"; }

            var sum = EvalScorer.Summarize(scores);
            RhinoApp.WriteLine($"MANTIS eval complete — parsed {sum.Parsed}/{sum.Total}, "
                               + $"clean {sum.ValidatedClean}/{sum.Total}, resolved {sum.AllNamesResolved}/{sum.Total}, "
                               + $"mean structural {sum.MeanStructuralScore:0.000}. Report: {path}");
        });

        return Result.Success; // return immediately — the UI never blocks
    }
}
