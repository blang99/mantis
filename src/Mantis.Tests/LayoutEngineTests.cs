using Mantis.Plugin.ScriptBuilder;
using Xunit;

namespace Mantis.Tests;

public class LayoutEngineTests
{
    [Fact]
    public void ComputeLayout_LinearChain_AssignsIncreasingColumns()
    {
        var engine = new LayoutEngine();
        var script = new ScriptDefinition
        {
            Components = new()
            {
                new() { Id = 1, Name = "A" },
                new() { Id = 2, Name = "B" },
                new() { Id = 3, Name = "C" }
            },
            Connections = new()
            {
                new() { FromComponent = 1, FromOutput = 0, ToComponent = 2, ToInput = 0 },
                new() { FromComponent = 2, FromOutput = 0, ToComponent = 3, ToInput = 0 }
            }
        };

        var positions = engine.ComputeLayout(script);

        Assert.Equal(3, positions.Count);
        Assert.True(positions[1].X < positions[2].X);
        Assert.True(positions[2].X < positions[3].X);
    }

    [Fact]
    public void ComputeLayout_ParallelInputs_SameColumn()
    {
        var engine = new LayoutEngine();
        var script = new ScriptDefinition
        {
            Components = new()
            {
                new() { Id = 1, Name = "Input A" },
                new() { Id = 2, Name = "Input B" },
                new() { Id = 3, Name = "Merge" }
            },
            Connections = new()
            {
                new() { FromComponent = 1, FromOutput = 0, ToComponent = 3, ToInput = 0 },
                new() { FromComponent = 2, FromOutput = 0, ToComponent = 3, ToInput = 1 }
            }
        };

        var positions = engine.ComputeLayout(script);

        Assert.Equal(positions[1].X, positions[2].X);
        Assert.True(positions[3].X > positions[1].X);
    }

    [Fact]
    public void ComputeLayout_DisconnectedComponents_AllGetPositions()
    {
        var engine = new LayoutEngine();
        var script = new ScriptDefinition
        {
            Components = new()
            {
                new() { Id = 1, Name = "Standalone A" },
                new() { Id = 2, Name = "Standalone B" },
                new() { Id = 3, Name = "Standalone C" }
            },
            Connections = new()
        };

        var positions = engine.ComputeLayout(script);

        Assert.Equal(3, positions.Count);
        Assert.True(positions.ContainsKey(1));
        Assert.True(positions.ContainsKey(2));
        Assert.True(positions.ContainsKey(3));
    }

    [Fact]
    public void ComputeLayout_EmptyScript_ReturnsEmpty()
    {
        var engine = new LayoutEngine();
        var script = new ScriptDefinition();

        var positions = engine.ComputeLayout(script);

        Assert.Empty(positions);
    }

    [Fact]
    public void ComputeLayout_NoOverlap_PositionsAreSeparated()
    {
        var engine = new LayoutEngine();
        var script = new ScriptDefinition
        {
            Components = new()
            {
                new() { Id = 1, Name = "A" },
                new() { Id = 2, Name = "B" },
                new() { Id = 3, Name = "C" },
                new() { Id = 4, Name = "D" }
            },
            Connections = new()
            {
                new() { FromComponent = 1, FromOutput = 0, ToComponent = 3, ToInput = 0 },
                new() { FromComponent = 2, FromOutput = 0, ToComponent = 3, ToInput = 1 },
                new() { FromComponent = 3, FromOutput = 0, ToComponent = 4, ToInput = 0 }
            }
        };

        var positions = engine.ComputeLayout(script);

        var allPositions = positions.Values.ToList();
        for (int i = 0; i < allPositions.Count; i++)
        {
            for (int j = i + 1; j < allPositions.Count; j++)
            {
                var dist = Math.Sqrt(
                    Math.Pow(allPositions[i].X - allPositions[j].X, 2) +
                    Math.Pow(allPositions[i].Y - allPositions[j].Y, 2));
                Assert.True(dist > 50, $"Components {i} and {j} are too close: {dist:F1}px apart");
            }
        }
    }

    [Fact]
    public void GetIncrementalPosition_DifferentCounts_DifferentPositions()
    {
        var engine = new LayoutEngine();

        var p0 = engine.GetIncrementalPosition(1, 0);
        var p1 = engine.GetIncrementalPosition(2, 1);
        var p2 = engine.GetIncrementalPosition(3, 2);

        Assert.NotEqual(p0, p1);
        Assert.NotEqual(p1, p2);
    }
}
