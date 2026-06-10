using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Mantis.Plugin.Knowledge;

/// <summary>
/// PHASE 5 — OPT-IN cross-user learning. When (and ONLY when) the user has explicitly turned
/// on sharing AND a backend endpoint is configured, MANTIS shares its anonymized, geometry-free
/// lessons and pulls the community bundle, so a mistake one user hit is one no user repeats.
///
/// INERT BY DEFAULT. With no consent + no endpoint this class makes ZERO network calls — the
/// "nothing leaves your machine" guarantee holds untouched until the user deliberately opts in.
/// What's shared is ONLY the generalized correction (trigger + remedy + tags) plus an opaque,
/// rotating-capable install id (a random GUID — no account, no email, no geometry, no prompt text).
/// All calls are best-effort and swallow errors so they can never affect a build.
/// </summary>
public static class LessonSync
{
    public const string ConsentKey = "shareLessons";        // "on" to opt in (default: off)
    public const string EndpointKey = "lessonSyncEndpoint";  // backend base URL (default: empty)
    public const string InstallIdKey = "installId";          // opaque random id, no PII

    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    /// <summary>True only when the user opted in AND configured a backend URL.</summary>
    public static bool Enabled =>
        MantisSettings.Get(ConsentKey) == "on" &&
        !string.IsNullOrWhiteSpace(MantisSettings.Get(EndpointKey));

    /// <summary>An opaque, per-install random id (no PII). Created lazily on first use.</summary>
    private static string InstallId
    {
        get
        {
            var id = MantisSettings.Get(InstallIdKey);
            if (string.IsNullOrWhiteSpace(id))
            {
                id = Guid.NewGuid().ToString("N");
                MantisSettings.Set(InstallIdKey, id);
            }
            return id!;
        }
    }

    /// <summary>Fire-and-forget: share one anonymized lesson if (and only if) sharing is enabled.</summary>
    public static void TryShare(Lesson? lesson)
    {
        if (!Enabled || lesson == null || string.IsNullOrWhiteSpace(lesson.Remedy)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                var url = MantisSettings.Get(EndpointKey)!.TrimEnd('/') + "/lessons";
                var payload = JsonSerializer.Serialize(new
                {
                    installId = InstallId,
                    key = lesson.Key,
                    trigger = lesson.Trigger,
                    remedy = lesson.Remedy,
                    tags = lesson.Tags
                });
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                await _http.PostAsync(url, content);
            }
            catch { /* best-effort; never affect the build */ }
        });
    }

    /// <summary>Pull the community lesson bundle and merge it into the local store (best-effort).</summary>
    public static async Task PullAsync(LessonStore store)
    {
        if (!Enabled || store == null) return;
        try
        {
            var url = MantisSettings.Get(EndpointKey)!.TrimEnd('/') + "/bundle";
            var json = await _http.GetStringAsync(url);
            var shared = JsonSerializer.Deserialize<List<Lesson>>(json, _json);
            if (shared == null) return;
            foreach (var l in shared)
                store.Record(l.Key, l.Trigger, l.Remedy, l.Tags);
        }
        catch { /* offline / endpoint down — keep using local lessons */ }
    }
}
