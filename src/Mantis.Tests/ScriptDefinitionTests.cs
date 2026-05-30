using System.Text.Json;
using Mantis.Plugin.ScriptBuilder;
using Xunit;

namespace Mantis.Tests;

public class ScriptDefinitionTests
{
    [Fact]
    public void Deserialize_FullScript_AllFieldsPopulated()
    {
        var json = """
        {
          "solutionName": "Tower",
          "solutionDescription": "A parametric tower",
          "components": [
            {
              "id": 1,
              "name": "Number Slider",
              "nickName": "Height",
              "inputValues": { "0": { "min": 0, "max": 100, "value": 50, "decimals": 0 } }
            },
            {
              "id": 2,
              "name": "Extrude",
              "inputValues": null
            }
          ],
          "connections": [
            {
              "fromComponent": 1,
              "fromOutput": 0,
              "toComponent": 2,
              "toInput": 1
            }
          ],
          "advice": "Adjust height slider."
        }
        """;

        var script = JsonSerializer.Deserialize<ScriptDefinition>(json);

        Assert.NotNull(script);
        Assert.Equal("Tower", script.SolutionName);
        Assert.Equal(2, script.Components.Count);
        Assert.Equal("Number Slider", script.Components[0].Name);
        Assert.Equal("Height", script.Components[0].NickName);
        Assert.NotNull(script.Components[0].InputValues);
        Assert.Null(script.Components[1].InputValues);
        Assert.Single(script.Connections);
        Assert.Equal(1, script.Connections[0].FromComponent);
        Assert.Equal(2, script.Connections[0].ToComponent);
    }

    [Fact]
    public void Deserialize_MultiSolution_ParsesAllSolutions()
    {
        var json = """
        {
          "solutions": [
            {
              "solutionName": "A",
              "solutionDescription": "First",
              "components": [{ "id": 1, "name": "Circle CNR" }],
              "connections": [],
              "advice": ""
            },
            {
              "solutionName": "B",
              "solutionDescription": "Second",
              "components": [{ "id": 1, "name": "Rectangle" }],
              "connections": [],
              "advice": ""
            }
          ],
          "comparisonSummary": "Circle vs Rectangle"
        }
        """;

        var multi = JsonSerializer.Deserialize<MultiSolutionResponse>(json);

        Assert.NotNull(multi);
        Assert.Equal(2, multi.Solutions.Count);
        Assert.Equal("A", multi.Solutions[0].SolutionName);
        Assert.Equal("B", multi.Solutions[1].SolutionName);
        Assert.Equal("Circle vs Rectangle", multi.ComparisonSummary);
    }

    [Fact]
    public void Serialize_RoundTrip_Preserves()
    {
        var original = new ScriptDefinition
        {
            SolutionName = "Test",
            SolutionDescription = "Roundtrip",
            Components = new List<ComponentDef>
            {
                new() { Id = 1, Name = "Move", NickName = "Mv" },
                new() { Id = 2, Name = "Scale" }
            },
            Connections = new List<ConnectionDef>
            {
                new() { FromComponent = 1, FromOutput = 0, ToComponent = 2, ToInput = 0 }
            },
            Advice = "Done"
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<ScriptDefinition>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.SolutionName, deserialized.SolutionName);
        Assert.Equal(original.Components.Count, deserialized.Components.Count);
        Assert.Equal(original.Connections.Count, deserialized.Connections.Count);
    }

    [Fact]
    public void ConnectionDef_OptionalNames_CanBeNull()
    {
        var json = """
        {
          "fromComponent": 1,
          "fromOutput": 0,
          "toComponent": 2,
          "toInput": 1
        }
        """;

        var conn = JsonSerializer.Deserialize<ConnectionDef>(json);

        Assert.NotNull(conn);
        Assert.Null(conn.FromOutputName);
        Assert.Null(conn.ToInputName);
    }

    [Fact]
    public void ComponentDef_WithPosition_Parses()
    {
        var json = """
        {
          "id": 5,
          "name": "Loft",
          "position": { "x": 300.5, "y": 150.0 }
        }
        """;

        var comp = JsonSerializer.Deserialize<ComponentDef>(json);

        Assert.NotNull(comp);
        Assert.NotNull(comp.Position);
        Assert.Equal(300.5, comp.Position.X);
        Assert.Equal(150.0, comp.Position.Y);
    }
}
