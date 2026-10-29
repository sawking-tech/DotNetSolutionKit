using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NamespaceRoot.ProductName.Common.Contracts.Health;

/// <summary>
/// The version and release notes from <c>version.json</c>, which the build copies next to the service's
/// binaries: the current entry is what <c>/health</c> reports, and <see cref="History"/> holds every entry.
/// Falls back to <see cref="Empty"/> when the file is missing.
/// </summary>
public sealed record VersionInfo(
    string Version,
    ReleaseNotes? ReleaseNotes,
    IReadOnlyList<VersionedReleaseNotes> History)
{
    public static readonly VersionInfo Empty = new("unknown", null, []);

    private static readonly Lazy<VersionInfo> _current = new(
        () => LoadFromFile(Path.Combine(AppContext.BaseDirectory, "version.json")),
        isThreadSafe: true);

    public static VersionInfo Current => _current.Value;

    private static readonly Lazy<string> _commit = new(ResolveCommit, isThreadSafe: true);

    /// <summary>
    /// Resolved commit identifier surfaced on <c>/health</c>. Resolution order:
    /// <list type="number">
    ///   <item><c>GIT_SHA</c> environment variable (set in deployed images via Dockerfile build-arg).</item>
    ///   <item>Suffix after <c>+</c> in <see cref="AssemblyInformationalVersionAttribute"/> on the entry assembly
    ///   (set at build time by the <c>SetSourceRevisionId</c> target in <c>Directory.Build.props</c>).</item>
    ///   <item>Literal <c>"local"</c> when neither is available.</item>
    /// </list>
    /// </summary>
    public static string CurrentCommit => _commit.Value;

    private static string ResolveCommit()
    {
        var fromEnv = Environment.GetEnvironmentVariable("GIT_SHA");
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return fromEnv;

        var fromBuild = Assembly.GetEntryAssembly()
            ?.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "GitCommit")
            ?.Value;

        return string.IsNullOrWhiteSpace(fromBuild) ? "local" : fromBuild;
    }

    /// <summary>
    /// Load and parse a version manifest from the given path. Returns <see cref="Empty"/> when
    /// the file is missing, malformed, or has no <c>version</c> field. Public for unit testing -
    /// production code uses <see cref="Current"/>.
    /// </summary>
    public static VersionInfo LoadFromFile(string path)
    {
        if (!File.Exists(path))
            return Empty;

        try
        {
            using var stream = File.OpenRead(path);
            return Parse(stream);
        }
        catch
        {
            return Empty;
        }
    }

    /// <summary>
    /// Parse a version manifest from a stream. Returns <see cref="Empty"/> when the document is
    /// malformed or missing the <c>version</c> field. Exposed for testing with in-memory inputs.
    /// </summary>
    public static VersionInfo Parse(Stream stream)
    {
        try
        {
            var doc = JsonSerializer.Deserialize<VersionDocument>(stream, JsonOptions);
            if (doc is null || string.IsNullOrEmpty(doc.Version))
                return Empty;

            var history = doc.ReleaseNotes
                .Select(kv => new VersionedReleaseNotes(kv.Key, kv.Value))
                .OrderByDescending(v => v.Version, VersionComparer.Instance)
                .ToList();

            doc.ReleaseNotes.TryGetValue(doc.Version, out var current);
            return new VersionInfo(doc.Version, current, history);
        }
        catch
        {
            return Empty;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class VersionDocument
    {
        public string Version { get; set; } = "";
        public Dictionary<string, ReleaseNotes> ReleaseNotes { get; set; } = new();
    }

    private sealed class VersionComparer : IComparer<string>
    {
        public static readonly VersionComparer Instance = new();

        public int Compare(string? x, string? y)
        {
            if (System.Version.TryParse(x, out var vx) && System.Version.TryParse(y, out var vy))
                return vx.CompareTo(vy);
            return StringComparer.Ordinal.Compare(x, y);
        }
    }
}

public sealed record ReleaseNotes(
    [property: JsonPropertyName("headline")] string Headline,
    [property: JsonPropertyName("highlights")] IReadOnlyList<string> Highlights);

public sealed record VersionedReleaseNotes(string Version, ReleaseNotes Notes);
