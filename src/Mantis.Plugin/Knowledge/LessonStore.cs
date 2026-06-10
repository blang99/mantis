using System.Text;
using System.Text.Json;

namespace Mantis.Plugin.Knowledge;

/// <summary>
/// A generalized, geometry-free correction MANTIS learned from a past build — a trigger
/// (what went wrong) paired with a remedy (what to do instead). Contains catalog/wiring
/// corrections only, never the user's geometry or design.
/// </summary>
public class Lesson
{
    public string Key { get; set; } = "";       // dedupe key
    public string Trigger { get; set; } = "";    // human-readable situation
    public string Remedy { get; set; } = "";     // what to do instead
    public int Count { get; set; } = 1;          // times reinforced
    public string Tags { get; set; } = "";       // keywords for relevance scoring
}

/// <summary>
/// PHASE 4 — MANTIS's LOCAL memory. Captures generalized corrections (a bad component name →
/// the right one, port limits, successful heals) into <c>&lt;AppData&gt;/Mantis/lessons.json</c> and
/// feeds the most relevant ones back into future prompts, so MANTIS stops repeating its own
/// mistakes and gets more efficient over time.
///
/// Same privacy class as settings.json — geometry-free, per-user, never leaves the machine.
/// (Phase 5 will optionally sync anonymized lessons across users WITH consent.)
/// </summary>
public class LessonStore
{
    private static readonly Lazy<LessonStore> _shared = new(() => new LessonStore());
    public static LessonStore Shared => _shared.Value;

    private static readonly JsonSerializerOptions _json = new() { WriteIndented = true };
    private readonly string _path;
    private List<Lesson> _lessons = new();
    private bool _loaded;

    public LessonStore(string? path = null)
    {
        _path = path ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mantis", "lessons.json");
    }

    public IReadOnlyList<Lesson> All { get { EnsureLoaded(); return _lessons; } }

    /// <summary>Add a new lesson or reinforce an existing one (matched by key). Persists immediately.</summary>
    public void Record(string key, string trigger, string remedy, string tags = "")
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(remedy)) return;
        EnsureLoaded();
        var existing = _lessons.FirstOrDefault(l => l.Key == key);
        if (existing != null)
        {
            existing.Count++;
            existing.Remedy = remedy;
            existing.Trigger = trigger;
        }
        else
        {
            _lessons.Add(new Lesson { Key = key, Trigger = trigger, Remedy = remedy, Count = 1, Tags = tags });
        }
        Save();
    }

    /// <summary>The top-K lessons most relevant to <paramref name="text"/> (keyword overlap, then reinforcement).</summary>
    public IReadOnlyList<Lesson> GetRelevant(string? text, int k)
    {
        EnsureLoaded();
        if (_lessons.Count == 0 || k <= 0) return Array.Empty<Lesson>();
        var words = Tokenize(text);
        return _lessons
            .OrderByDescending(l => Overlap(l, words))
            .ThenByDescending(l => l.Count)
            .Take(k)
            .ToList();
    }

    /// <summary>Prompt block of the top-K relevant lessons ("- trigger → remedy"), or "" if none.</summary>
    public string BuildPromptBlock(string? text, int k)
    {
        var relevant = GetRelevant(text, k);
        if (relevant.Count == 0) return "";
        var sb = new StringBuilder();
        foreach (var l in relevant)
            sb.Append("- ").Append(l.Trigger).Append(" → ").AppendLine(l.Remedy);
        return sb.ToString();
    }

    /// <summary>Forget everything (clears the file).</summary>
    public void Clear()
    {
        _lessons = new();
        _loaded = true;
        try { if (File.Exists(_path)) File.Delete(_path); } catch { }
    }

    private static HashSet<string> Tokenize(string? text)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text)) return set;
        foreach (var w in text.Split(
            new[] { ' ', ',', '.', '"', '\'', '(', ')', '\n', '\r', '\t', '/', '-', ':' },
            StringSplitOptions.RemoveEmptyEntries))
            if (w.Length >= 3) set.Add(w);
        return set;
    }

    private static int Overlap(Lesson l, HashSet<string> words)
    {
        if (words.Count == 0) return 0;
        int score = 0;
        foreach (var t in Tokenize(l.Tags + " " + l.Trigger))
            if (words.Contains(t)) score++;
        return score;
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        try
        {
            if (File.Exists(_path))
                _lessons = JsonSerializer.Deserialize<List<Lesson>>(File.ReadAllText(_path)) ?? new();
        }
        catch { _lessons = new(); }
    }

    private void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(_lessons, _json));
        }
        catch { }
    }
}
