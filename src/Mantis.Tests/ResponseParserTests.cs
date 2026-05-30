using Mantis.Plugin.AI;
using Mantis.Plugin.ScriptBuilder;
using Xunit;

namespace Mantis.Tests;

public class ResponseParserTests
{
    [Fact]
    public void ParseComplete_ValidScript_ReturnsDefinition()
    {
        var parser = new ResponseParser();
        var json = """
        {
          "solutionName": "Simple Circle",
          "solutionDescription": "A circle with adjustable radius",
          "components": [
            { "id": 1, "name": "Number Slider", "nickName": "Radius", "inputValues": { "0": { "min": 0, "max": 20, "value": 5, "decimals": 1 } } },
            { "id": 2, "name": "Circle CNR" }
          ],
          "connections": [
            { "fromComponent": 1, "fromOutput": 0, "toComponent": 2, "toInput": 1 }
          ],
          "advice": "Adjust the slider to change radius."
        }
        """;

        var result = parser.ParseComplete(json);

        Assert.NotNull(result);
        Assert.Equal("Simple Circle", result.SolutionName);
        Assert.Equal(2, result.Components.Count);
        Assert.Single(result.Connections);
        Assert.Equal("Number Slider", result.Components[0].Name);
        Assert.Equal("Circle CNR", result.Components[1].Name);
    }

    [Fact]
    public void ParseComplete_MultiSolution_ReturnsFistSolution()
    {
        var parser = new ResponseParser();
        var json = """
        {
          "solutions": [
            {
              "solutionName": "Approach A",
              "solutionDescription": "Using loft",
              "components": [{ "id": 1, "name": "Loft" }],
              "connections": [],
              "advice": "Loft approach"
            },
            {
              "solutionName": "Approach B",
              "solutionDescription": "Using extrude",
              "components": [{ "id": 1, "name": "Extrude" }],
              "connections": [],
              "advice": "Extrude approach"
            }
          ],
          "comparisonSummary": "Loft is more flexible, Extrude is simpler."
        }
        """;

        var result = parser.ParseComplete(json);

        Assert.NotNull(result);
        Assert.Equal("Approach A", result.SolutionName);
    }

    [Fact]
    public void ParseComplete_InvalidJson_ReturnsNull()
    {
        var parser = new ResponseParser();
        var result = parser.ParseComplete("not json at all");
        Assert.Null(result);
    }

    [Fact]
    public void ParseComplete_EmptyComponents_ReturnsNull()
    {
        var parser = new ResponseParser();
        var json = """
        {
          "solutionName": "Empty",
          "components": [],
          "connections": []
        }
        """;

        var result = parser.ParseComplete(json);
        Assert.Null(result);
    }

    [Fact]
    public void Feed_StreamingChunks_FiresScriptComplete()
    {
        var parser = new ResponseParser();
        ScriptDefinition? captured = null;
        parser.OnScriptComplete += s => captured = s;

        var json = """{"solutionName":"Test","solutionDescription":"","components":[{"id":1,"name":"Circle CNR"}],"connections":[],"advice":""}""";

        // Simulate streaming in chunks
        parser.Feed("Here is the script: ");
        parser.Feed(json[..20]);
        parser.Feed(json[20..50]);
        parser.Feed(json[50..]);

        Assert.NotNull(captured);
        Assert.Equal("Test", captured.SolutionName);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var parser = new ResponseParser();
        parser.Feed("{\"partial\": ");
        parser.Reset();

        ScriptDefinition? captured = null;
        parser.OnScriptComplete += s => captured = s;

        var json = """{"solutionName":"After Reset","solutionDescription":"","components":[{"id":1,"name":"Move"}],"connections":[],"advice":""}""";
        parser.Feed(json);

        Assert.NotNull(captured);
        Assert.Equal("After Reset", captured.SolutionName);
    }
}
