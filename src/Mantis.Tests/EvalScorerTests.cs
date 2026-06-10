using Mantis.Plugin.Eval;
using Mantis.Plugin.ScriptBuilder;
using Xunit;

namespace Mantis.Tests;

/// <summary>
/// The eval harness's deterministic half — turns "does MANTIS work?" into a number. Verifies the
/// scorer rewards on-target builds, FLAGS unresolved/hallucinated names (the silent-mis-resolution
/// failure the review called out), penalizes missing intent traits, and aggregates by tier.
/// </summary>
public class EvalScorerTests
{
    private static readonly HashSet<string> Catalog = new(StringComparer.OrdinalIgnoreCase)
        { "Number Slider", "Circle", "Series", "Move", "Loft" };
    private static bool CanResolve(string n) => Catalog.Contains(n);

    private static ScriptDefinition Script(params (int id, string name)[] comps)
    {
        var s = new ScriptDefinition();
        foreach (var (id, name) in comps) s.Components.Add(new ComponentDef { Id = id, Name = name });
        return s;
    }

    [Fact]
    public void Clean_on_target_build_scores_perfect()
    {
        var c = new EvalCase { Id = "a01", Tier = "array", MinComponents = 3, Traits = new() { "Circle", "Series", "Move" } };
        var script = Script((1, "Number Slider"), (2, "Circle"), (3, "Series"), (4, "Move"));
        var s = EvalScorer.ScoreStructural(c, script, CanResolve, validatedClean: true);

        Assert.True(s.Parsed);
        Assert.True(s.AllNamesResolved);
        Assert.True(s.MeetsMinComponents);
        Assert.Equal(1.0, s.TraitCoverage);
        Assert.Equal(1.0, s.StructuralScore);
    }

    [Fact]
    public void Hallucinated_name_is_flagged_not_hidden()
    {
        var c = new EvalCase { Id = "x", Tier = "t", MinComponents = 1, Traits = new() { "Circle" } };
        var script = Script((1, "Circle"), (2, "Spherical Surface")); // not in catalog
        var s = EvalScorer.ScoreStructural(c, script, CanResolve, validatedClean: false);

        Assert.False(s.AllNamesResolved);
        Assert.Contains("Spherical Surface", s.UnresolvedNames);
        Assert.True(s.StructuralScore < 1.0);
    }

    [Fact]
    public void Missing_traits_reduce_coverage()
    {
        var c = new EvalCase { Id = "m", Tier = "multistage", MinComponents = 2, Traits = new() { "Circle", "Series", "Loft" } };
        var script = Script((1, "Circle"), (2, "Series")); // no Loft
        var s = EvalScorer.ScoreStructural(c, script, CanResolve, validatedClean: true);

        Assert.Contains("Loft", s.MissingTraits);
        Assert.Equal(2.0 / 3.0, s.TraitCoverage, 3);
    }

    [Fact]
    public void Null_or_empty_script_scores_zero()
    {
        var c = new EvalCase { Id = "n", Tier = "t", MinComponents = 1, Traits = new() { "Circle" } };
        var s = EvalScorer.ScoreStructural(c, null, CanResolve, validatedClean: false);
        Assert.False(s.Parsed);
        Assert.Equal(0.0, s.StructuralScore);
        Assert.False(s.LivePass);
    }

    [Fact]
    public void Summarize_aggregates_headline_and_per_tier()
    {
        var scores = new List<EvalScore>
        {
            new() { Id = "1", Tier = "primitive", Parsed = true, ValidatedClean = true, AllNamesResolved = true, MeetsMinComponents = true, TraitCoverage = 1, SolvedNoErrors = true, ProducedGeometry = true },
            new() { Id = "2", Tier = "primitive", Parsed = true, ValidatedClean = false },
        };
        var sum = EvalScorer.Summarize(scores);

        Assert.Equal(2, sum.Total);
        Assert.Equal(2, sum.Parsed);
        Assert.Equal(1, sum.LivePass);
        Assert.True(sum.ByTier.ContainsKey("primitive"));
        Assert.Equal(2, sum.ByTier["primitive"].Count);
    }
}
