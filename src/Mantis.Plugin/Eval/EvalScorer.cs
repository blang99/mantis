using Mantis.Plugin.ScriptBuilder;

namespace Mantis.Plugin.Eval;

/// <summary>
/// Pure, headless scoring of a generated script against an eval case — the deterministic half of
/// the eval: parse / validate-clean / names-resolve / min-components / trait-coverage. The live
/// half (solved with no runtime errors, produced real geometry) is added by the in-Rhino runner.
/// No Grasshopper dependency, so it is unit-tested directly.
/// </summary>
public static class EvalScorer
{
    /// <param name="canResolve">the SAME name-resolution predicate the plugin uses (ComponentFactory.CanResolve).</param>
    /// <param name="validatedClean">true if ScriptValidator reported zero error-severity issues.</param>
    public static EvalScore ScoreStructural(EvalCase c, ScriptDefinition? script, Func<string, bool> canResolve, bool validatedClean)
    {
        var s = new EvalScore { Id = c.Id, Tier = c.Tier };
        if (script == null || script.Components.Count == 0)
            return s; // Parsed = false → StructuralScore 0

        s.Parsed = true;
        s.ComponentCount = script.Components.Count;
        s.MeetsMinComponents = script.Components.Count >= c.MinComponents;
        s.ValidatedClean = validatedClean;

        foreach (var comp in script.Components)
            if (!canResolve(comp.Name))
                s.UnresolvedNames.Add(comp.Name);
        s.AllNamesResolved = s.UnresolvedNames.Count == 0;

        int hit = 0;
        foreach (var trait in c.Traits)
        {
            if (script.Components.Any(comp => TraitMatches(comp, trait))) hit++;
            else s.MissingTraits.Add(trait);
        }
        s.TraitCoverage = c.Traits.Count == 0 ? 1.0 : (double)hit / c.Traits.Count;

        return s;
    }

    private static bool TraitMatches(ComponentDef comp, string trait)
    {
        return string.Equals(comp.Name, trait, StringComparison.OrdinalIgnoreCase)
            || string.Equals(comp.NickName, trait, StringComparison.OrdinalIgnoreCase)
            || (comp.Name?.Contains(trait, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <summary>Aggregate a run into a one-line-per-tier scorecard plus headline numbers.</summary>
    public static EvalSummary Summarize(IReadOnlyList<EvalScore> scores)
    {
        var sum = new EvalSummary { Total = scores.Count };
        if (scores.Count == 0) return sum;

        sum.Parsed = scores.Count(s => s.Parsed);
        sum.ValidatedClean = scores.Count(s => s.ValidatedClean);
        sum.AllNamesResolved = scores.Count(s => s.AllNamesResolved);
        sum.LivePass = scores.Count(s => s.LivePass);
        sum.MeanStructuralScore = scores.Average(s => s.StructuralScore);

        foreach (var grp in scores.GroupBy(s => s.Tier))
            sum.ByTier[grp.Key] = new TierStat
            {
                Count = grp.Count(),
                MeanStructural = grp.Average(s => s.StructuralScore),
                LivePass = grp.Count(s => s.LivePass),
            };
        return sum;
    }
}

public class EvalSummary
{
    public int Total { get; set; }
    public int Parsed { get; set; }
    public int ValidatedClean { get; set; }
    public int AllNamesResolved { get; set; }
    public int LivePass { get; set; }
    public double MeanStructuralScore { get; set; }
    public Dictionary<string, TierStat> ByTier { get; set; } = new();
}

public class TierStat
{
    public int Count { get; set; }
    public double MeanStructural { get; set; }
    public int LivePass { get; set; }
}
