using System.Text.Json.Serialization;

namespace Mantis.Plugin.Eval;

/// <summary>One frozen eval case: a prompt + the traits a correct build should exhibit.</summary>
public class EvalCase
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("tier")] public string Tier { get; set; } = "";
    [JsonPropertyName("prompt")] public string Prompt { get; set; } = "";
    [JsonPropertyName("minComponents")] public int MinComponents { get; set; }
    [JsonPropertyName("traits")] public List<string> Traits { get; set; } = new();
    [JsonPropertyName("geometry")] public string Geometry { get; set; } = "";
}

/// <summary>
/// The score for one eval case. The headless dimensions are filled by <see cref="EvalScorer"/>;
/// the LIVE dimensions (<see cref="SolvedNoErrors"/>, <see cref="ProducedGeometry"/>) are filled
/// by the in-Rhino runner after an actual Grasshopper solve. This is the unit that converts
/// "does MANTIS work?" from opinion into a number.
/// </summary>
public class EvalScore
{
    public string Id { get; set; } = "";
    public string Tier { get; set; } = "";

    public bool Parsed { get; set; }            // model produced a usable script
    public bool ValidatedClean { get; set; }    // ScriptValidator: zero error-severity issues
    public bool AllNamesResolved { get; set; }  // every component resolves to a real catalog name
    public int ComponentCount { get; set; }
    public bool MeetsMinComponents { get; set; }
    public double TraitCoverage { get; set; }   // fraction of expected traits present
    public List<string> MissingTraits { get; set; } = new();
    public List<string> UnresolvedNames { get; set; } = new();

    // LIVE (in-Rhino) dimensions — default false when scored headlessly.
    public bool SolvedNoErrors { get; set; }
    public bool ProducedGeometry { get; set; }

    /// <summary>0..1 structural score from the headless dimensions (a failed parse gates it to 0).</summary>
    public double StructuralScore =>
        !Parsed ? 0.0
        : 0.30 * (ValidatedClean ? 1 : 0)
        + 0.30 * (AllNamesResolved ? 1 : 0)
        + 0.20 * (MeetsMinComponents ? 1 : 0)
        + 0.20 * TraitCoverage;

    /// <summary>The headline pass: structurally sound AND (when run live) solved with real geometry.</summary>
    public bool LivePass => Parsed && ValidatedClean && AllNamesResolved && SolvedNoErrors && ProducedGeometry;
}
