using System.Text;

namespace Mantis.Plugin.Eval;

/// <summary>Renders an eval run into a markdown scorecard (headline + per-tier + per-case).</summary>
public static class EvalReport
{
    public static string Render(IReadOnlyList<EvalScore> scores, string provider, string timestamp)
    {
        var sum = EvalScorer.Summarize(scores);
        var sb = new StringBuilder();

        sb.AppendLine($"# MANTIS eval — {timestamp}");
        sb.AppendLine();
        sb.AppendLine($"Provider: **{provider}** · cases: **{sum.Total}**");
        sb.AppendLine();
        sb.AppendLine($"- Parsed: **{sum.Parsed}/{sum.Total}**");
        sb.AppendLine($"- Validated clean (zero errors): **{sum.ValidatedClean}/{sum.Total}**");
        sb.AppendLine($"- All names resolved: **{sum.AllNamesResolved}/{sum.Total}**");
        sb.AppendLine($"- Live pass (solved + geometry): **{sum.LivePass}/{sum.Total}**");
        sb.AppendLine($"- Mean structural score: **{sum.MeanStructuralScore:0.000}**");
        sb.AppendLine();

        sb.AppendLine("## By tier");
        sb.AppendLine("| tier | n | mean structural | live pass |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var kv in sum.ByTier)
            sb.AppendLine($"| {kv.Key} | {kv.Value.Count} | {kv.Value.MeanStructural:0.000} | {kv.Value.LivePass}/{kv.Value.Count} |");
        sb.AppendLine();

        sb.AppendLine("## Per case");
        sb.AppendLine("| id | tier | struct | parsed | clean | resolved | traits | unresolved names |");
        sb.AppendLine("|---|---|---|---|---|---|---|---|");
        foreach (var s in scores)
            sb.AppendLine($"| {s.Id} | {s.Tier} | {s.StructuralScore:0.00} | {YN(s.Parsed)} | {YN(s.ValidatedClean)} | {YN(s.AllNamesResolved)} | {s.TraitCoverage:0.0} | {string.Join(" ", s.UnresolvedNames)} |");

        return sb.ToString();
    }

    private static string YN(bool b) => b ? "yes" : "NO";
}
