using Mantis.Plugin.ScriptBuilder;
using Xunit;

namespace Mantis.Tests;

/// <summary>
/// Unit tests for the shipping <see cref="ScriptValidator"/> — the structural
/// invariant checker now wired into MantisService's Generate and Iterate paths.
/// </summary>
public class ScriptValidatorTests
{
    private static bool ResolveAll(string _) => true;

    private static ComponentDef C(int id, string name = "X") => new() { Id = id, Name = name };
    private static ConnectionDef Wire(int from, int to) =>
        new() { FromComponent = from, ToComponent = to };
    private static GroupDef Group(string name, string reasoning, params int[] ids) =>
        new() { Name = name, Reasoning = reasoning, ComponentIds = ids.ToList() };

    [Fact]
    public void CleanScript_ProducesNoIssues()
    {
        var s = new ScriptDefinition
        {
            Components = { C(1, "Circle"), C(2, "Extrude") },
            Connections = { Wire(1, 2) },
            Groups = { Group("All", "circle, then extruded", 1, 2) }
        };

        var issues = ScriptValidator.Validate(s, ResolveAll);

        Assert.Empty(issues);
        Assert.False(ScriptValidator.HasErrors(issues));
    }

    [Fact]
    public void NullScript_IsError() =>
        Assert.Contains(ScriptValidator.Validate(null, ResolveAll), i => i.Code == "NULL_SCRIPT");

    [Fact]
    public void EmptyScript_IsError() =>
        Assert.Contains(ScriptValidator.Validate(new ScriptDefinition(), ResolveAll),
            i => i.Code == "EMPTY_SCRIPT" && i.Severity == IssueSeverity.Error);

    [Fact]
    public void DuplicateId_IsError()
    {
        var s = new ScriptDefinition { Components = { C(1), C(1) } };
        Assert.Contains(ScriptValidator.Validate(s, ResolveAll),
            i => i.Code == "DUPLICATE_ID" && i.Severity == IssueSeverity.Error);
    }

    [Fact]
    public void UnresolvableName_IsError()
    {
        var s = new ScriptDefinition { Components = { C(1, "Quantum Twist Engine") } };
        var issues = ScriptValidator.Validate(s, n => n != "Quantum Twist Engine");

        Assert.Contains(issues, i => i.Code == "UNRESOLVABLE_NAME"
                                     && i.Severity == IssueSeverity.Error
                                     && i.ComponentId == 1);
    }

    [Fact]
    public void DanglingConnection_IsError()
    {
        var s = new ScriptDefinition { Components = { C(1) }, Connections = { Wire(1, 99) } };
        Assert.Contains(ScriptValidator.Validate(s, ResolveAll),
            i => i.Code == "DANGLING_CONNECTION" && i.Message.Contains("99"));
    }

    [Fact]
    public void SelfConnection_IsWarning()
    {
        var s = new ScriptDefinition { Components = { C(1) }, Connections = { Wire(1, 1) } };
        var issues = ScriptValidator.Validate(s, ResolveAll);

        Assert.Contains(issues, i => i.Code == "SELF_CONNECTION" && i.Severity == IssueSeverity.Warning);
        Assert.False(ScriptValidator.HasErrors(issues)); // a self-wire is cosmetic, not fatal
    }

    [Fact]
    public void OrphanComponent_IsWarning_WhenStagesExist()
    {
        var s = new ScriptDefinition
        {
            Components = { C(1), C(2) },
            Groups = { Group("Only one", "covers just component 1", 1) }
        };
        Assert.Contains(ScriptValidator.Validate(s, ResolveAll),
            i => i.Code == "ORPHAN_COMPONENT" && i.ComponentId == 2);
    }

    [Fact]
    public void ComponentInTwoStages_IsWarning()
    {
        var s = new ScriptDefinition
        {
            Components = { C(1) },
            Groups = { Group("A", "r", 1), Group("B", "r", 1) }
        };
        Assert.Contains(ScriptValidator.Validate(s, ResolveAll),
            i => i.Code == "MULTI_GROUP" && i.ComponentId == 1);
    }

    [Fact]
    public void EmptyReasoning_IsWarning()
    {
        var s = new ScriptDefinition
        {
            Components = { C(1) },
            Groups = { Group("Silent", "   ", 1) }
        };
        Assert.Contains(ScriptValidator.Validate(s, ResolveAll), i => i.Code == "MISSING_REASONING");
    }

    [Fact]
    public void NoStages_DoesNotFlagCoverage()
    {
        // A legacy/simple response with no groups must not be treated as "all orphans".
        var s = new ScriptDefinition { Components = { C(1), C(2) }, Connections = { Wire(1, 2) } };
        var issues = ScriptValidator.Validate(s, ResolveAll);

        Assert.DoesNotContain(issues, i => i.Code == "ORPHAN_COMPONENT");
        Assert.Empty(issues);
    }

    // ─────────────────────────────  Port-range validation  ─────────────────────────────
    //  The validator checks each wire's port index against the SAME catalog arity the
    //  model was prompted with, so an out-of-range port is always the model wiring a
    //  port it was never offered. Arity is injected (a fake here) to stay GH-free.

    private static ConnectionDef WireP(int from, int fromOut, int to, int toIn) =>
        new() { FromComponent = from, FromOutput = fromOut, ToComponent = to, ToInput = toIn };

    /// <summary>Arity resolver from a name→(inputs,outputs) table; unknown names return null.</summary>
    private static Func<string, (int Inputs, int Outputs)?> Arity(
        params (string name, int inp, int outp)[] table) =>
        name =>
        {
            foreach (var t in table)
                if (t.name == name) return (t.inp, t.outp);
            return null;
        };

    [Fact]
    public void PortsInRange_ProduceNoPortError()
    {
        var s = new ScriptDefinition
        {
            Components = { C(1, "Circle"), C(2, "Extrude") },
            Connections = { WireP(1, 0, 2, 0) }
        };
        var issues = ScriptValidator.Validate(s, ResolveAll, Arity(("Circle", 0, 1), ("Extrude", 2, 1)));
        Assert.DoesNotContain(issues, i => i.Code == "PORT_OUT_OF_RANGE");
    }

    [Fact]
    public void FromOutputOutOfRange_IsError()
    {
        // Circle advertises ONE output (index 0); wiring FROM output 2 is impossible.
        var s = new ScriptDefinition
        {
            Components = { C(1, "Circle"), C(2, "Extrude") },
            Connections = { WireP(1, 2, 2, 0) }
        };
        var issues = ScriptValidator.Validate(s, ResolveAll, Arity(("Circle", 0, 1), ("Extrude", 2, 1)));
        Assert.Contains(issues, i => i.Code == "PORT_OUT_OF_RANGE"
                                     && i.Severity == IssueSeverity.Error
                                     && i.ComponentId == 1);
        Assert.True(ScriptValidator.HasErrors(issues));
    }

    [Fact]
    public void ToInputOutOfRange_IsError()
    {
        // Extrude advertises TWO inputs (0,1); wiring INTO input 5 is impossible.
        var s = new ScriptDefinition
        {
            Components = { C(1, "Circle"), C(2, "Extrude") },
            Connections = { WireP(1, 0, 2, 5) }
        };
        var issues = ScriptValidator.Validate(s, ResolveAll, Arity(("Circle", 0, 1), ("Extrude", 2, 1)));
        Assert.Contains(issues, i => i.Code == "PORT_OUT_OF_RANGE"
                                     && i.Severity == IssueSeverity.Error
                                     && i.ComponentId == 2);
    }

    [Fact]
    public void NegativePortIndex_IsError()
    {
        var s = new ScriptDefinition
        {
            Components = { C(1, "Circle"), C(2, "Extrude") },
            Connections = { WireP(1, -1, 2, 0) }
        };
        var issues = ScriptValidator.Validate(s, ResolveAll, Arity(("Circle", 0, 1), ("Extrude", 2, 1)));
        Assert.Contains(issues, i => i.Code == "PORT_OUT_OF_RANGE" && i.ComponentId == 1);
    }

    [Fact]
    public void ZeroCountSide_IsSkipped_NoFalsePositive()
    {
        // A Number Slider advertises 0 inputs. A 0-count side is treated as "unknown"
        // and never flagged, so missing catalog port data can't block a valid graph.
        var s = new ScriptDefinition
        {
            Components = { C(1, "Number Slider"), C(2, "Circle") },
            Connections = { WireP(2, 0, 1, 0) } // wired INTO the slider's (nonexistent) input 0
        };
        var issues = ScriptValidator.Validate(s, ResolveAll, Arity(("Number Slider", 0, 1), ("Circle", 0, 1)));
        Assert.DoesNotContain(issues, i => i.Code == "PORT_OUT_OF_RANGE");
    }

    [Fact]
    public void UnknownArity_SkipsPortCheck()
    {
        // Resolver returns null (arity unknown) → no port error even for a wild index.
        var s = new ScriptDefinition
        {
            Components = { C(1, "Mystery"), C(2, "Extrude") },
            Connections = { WireP(1, 99, 2, 0) }
        };
        var issues = ScriptValidator.Validate(s, ResolveAll, Arity(("Extrude", 2, 1))); // "Mystery" → null
        Assert.DoesNotContain(issues, i => i.Code == "PORT_OUT_OF_RANGE" && i.ComponentId == 1);
    }

    [Fact]
    public void NoArityResolver_NeverEmitsPortErrors()
    {
        // Back-compat: the 2-arg overload performs no port checks at all.
        var s = new ScriptDefinition
        {
            Components = { C(1, "Circle"), C(2, "Extrude") },
            Connections = { WireP(1, 99, 2, 99) }
        };
        var issues = ScriptValidator.Validate(s, ResolveAll);
        Assert.DoesNotContain(issues, i => i.Code == "PORT_OUT_OF_RANGE");
    }
}
