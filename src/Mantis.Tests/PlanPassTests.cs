using Mantis.Plugin.AI;
using Xunit;

namespace Mantis.Tests;

/// <summary>
/// Locks in the PLAN pass parsing — MANTIS understands the request and produces a
/// reasoned, ordered plan BEFORE building. The plan must survive markdown fences and
/// stray prose, and an empty/invalid plan must degrade to null (single-shot fallback).
/// </summary>
public class PlanPassTests
{
    private static readonly string ValidPlan = """
    {
      "intent": "Make a vertical stack of circles like a tower",
      "assumptions": ["world XY origin", "count from an integer slider"],
      "steps": [
        {"name":"Parameters","reasoning":"expose the knobs","approach":"sliders for radius/floors/height","candidateComponents":["Number Slider"]},
        {"name":"Base Floor","reasoning":"one circle to copy","approach":"Circle at origin","candidateComponents":["Circle"]},
        {"name":"Vertical Array","reasoning":"stack the floor","approach":"Series of heights into Move","candidateComponents":["Series","Unit Z","Move"]}
      ],
      "openQuestions": []
    }
    """;

    [Fact]
    public void ParsePlan_parses_a_valid_plan()
    {
        var plan = new ResponseParser().ParsePlan(ValidPlan);

        Assert.NotNull(plan);
        Assert.Equal("Make a vertical stack of circles like a tower", plan!.Intent);
        Assert.Equal(2, plan.Assumptions.Count);
        Assert.Equal(3, plan.Steps.Count);
        Assert.Equal("Parameters", plan.Steps[0].Name);
        Assert.Equal("Vertical Array", plan.Steps[2].Name);
        Assert.Contains("Move", plan.Steps[2].CandidateComponents);
    }

    [Fact]
    public void ParsePlan_survives_markdown_fences_and_prose()
    {
        var noisy = "Sure — here's my plan:\n```json\n" + ValidPlan + "\n```\nLet me know!";
        var plan = new ResponseParser().ParsePlan(noisy);

        Assert.NotNull(plan);
        Assert.Equal(3, plan!.Steps.Count);
    }

    [Fact]
    public void ParsePlan_returns_null_for_unusable_responses()
    {
        var parser = new ResponseParser();

        Assert.Null(parser.ParsePlan("I cannot help with that."));            // not JSON
        Assert.Null(parser.ParsePlan("{\"intent\":\"x\",\"steps\":[]}"));      // no steps = unusable -> fall back to single-shot
    }
}
