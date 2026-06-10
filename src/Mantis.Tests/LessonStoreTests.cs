using Mantis.Plugin.Knowledge;
using Xunit;

namespace Mantis.Tests;

/// <summary>
/// PHASE 4 — local lessons memory. Verifies MANTIS records, reinforces, ranks and recalls
/// generalized corrections (so it stops repeating its own mistakes), using an isolated temp
/// file so the test never touches the real %AppData%/Mantis/lessons.json.
/// </summary>
public class LessonStoreTests
{
    private static LessonStore FreshStore()
    {
        var path = Path.Combine(Path.GetTempPath(), "mantis-lessons-" + Guid.NewGuid().ToString("N") + ".json");
        return new LessonStore(path);
    }

    [Fact]
    public void Record_then_recall_persists_across_instances()
    {
        var path = Path.Combine(Path.GetTempPath(), "mantis-lessons-" + Guid.NewGuid().ToString("N") + ".json");
        new LessonStore(path).Record("name:circle cnr", "\"Circle CNR\" needs a center", "use \"Circle\"", "circle");

        var reloaded = new LessonStore(path);   // fresh instance reads from disk
        Assert.Single(reloaded.All);
        Assert.Equal("use \"Circle\"", reloaded.All[0].Remedy);
    }

    [Fact]
    public void Record_same_key_reinforces_not_duplicates()
    {
        var store = FreshStore();
        store.Record("name:foo", "\"Foo\" is not real", "use \"Bar\"", "foo bar");
        store.Record("name:foo", "\"Foo\" is not real", "use \"Bar\"", "foo bar");

        Assert.Single(store.All);
        Assert.Equal(2, store.All[0].Count);
    }

    [Fact]
    public void GetRelevant_prefers_keyword_overlap_then_reinforcement()
    {
        var store = FreshStore();
        store.Record("name:loft", "\"Loftt\" is not real", "use \"Loft\"", "loftt loft surface");
        store.Record("name:circle", "\"Circ\" is not real", "use \"Circle\"", "circ circle curve");

        var top = store.GetRelevant("build a lofted surface from curves", 1);
        Assert.Single(top);
        Assert.Equal("name:loft", top[0].Key);
    }

    [Fact]
    public void BuildPromptBlock_formats_and_Clear_empties()
    {
        var store = FreshStore();
        store.Record("name:foo", "\"Foo\" is not real", "use \"Bar\"", "foo");
        Assert.Contains("→", store.BuildPromptBlock("foo", 6));

        store.Clear();
        Assert.Empty(store.All);
        Assert.Equal("", store.BuildPromptBlock("foo", 6));
    }
}
