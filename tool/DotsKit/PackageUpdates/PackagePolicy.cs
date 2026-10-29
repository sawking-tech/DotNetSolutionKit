using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace DotsKit.PackageUpdates;

/// <summary>
/// How much of the template's package versions a solution takes, kept in its manifest: patches and minor
/// versions, patches only, or nothing; and packages the team holds on a version of its own. A major version of
/// a package comes only with a major version of the template.
/// </summary>
internal sealed record PackagePolicy
{
    public const string Minor = "minor";
    public const string Patch = "patch";
    public const string None = "none";

    [SuppressMessage(Suppressions.Performance, "CA1822", Justification = Suppressions.JsonMetadataKey)]
    [JsonPropertyName("_comment_updates")]
    public string UpdatesComment => "minor: patches and minor versions of packages come with an upgrade; patch: patches only; none: no package version changes";

    public string Updates { get; init; } = Minor;

    [SuppressMessage(Suppressions.Performance, "CA1822", Justification = Suppressions.JsonMetadataKey)]
    [JsonPropertyName("_comment_pinned")]
    public string PinnedComment => "Packages kept on the version given here, whatever the template brings; the upgrade report names each one";

    public Dictionary<string, string> Pinned { get; init; } = [];

    /// <summary>The version a package is pinned on, its name in any case, as NuGet reads package names.</summary>
    public string? PinOf(string package) =>
        Pinned.FirstOrDefault(p => string.Equals(p.Key, package, StringComparison.OrdinalIgnoreCase)).Value;

    /// <summary>Why the policy cannot be applied, or null; a manifest with an unknown value is refused, not guessed.</summary>
    public string? Problem =>
        Updates is Minor or Patch or None ? null : $"has packages.updates \"{Updates}\": it is {Minor}, {Patch} or {None}";

    /// <summary>
    /// The version a package takes: the pinned one; for a package the solution had, the template's new one as far
    /// as the policy lets it, else the one the solution had; a package new to the solution comes as the template has it.
    /// </summary>
    /// <param name="package">The package's name.</param>
    /// <param name="had">The version the solution had; null for a package new to it.</param>
    /// <param name="brought">The version the template gives now.</param>
    /// <param name="majorUpgrade">The template moves to its next major version: a package's major version may come with it.</param>
    public PackageDecision Decide(string package, string? had, string brought, bool majorUpgrade)
    {
        if (PinOf(package) is { } pinned)
        {
            return new(package, pinned, brought == pinned ? null : $"pinned on {pinned}, the template brings {brought}");
        }

        if (had is null || had == brought)
        {
            return new(package, brought, null);
        }

        if (!Version.TryParse(had.Split('-')[0], out var from) || !Version.TryParse(brought.Split('-')[0], out var to))
        {
            return new(package, brought, $"{had} -> {brought}");
        }

        var allowed = Updates switch
        {
            None => false,
            Patch => from.Major == to.Major && from.Minor == to.Minor,
            _ => from.Major == to.Major || majorUpgrade,
        };
        return allowed
            ? new(package, brought, $"{had} -> {brought}")
            : new(package, had, $"held on {had} by packages.updates \"{Updates}\", the template brings {brought}");
    }
}

/// <param name="Package">The package's name.</param>
/// <param name="Version">The version the package takes.</param>
/// <param name="Line">What the report says about it; null when nothing changes and nothing is held.</param>
internal sealed record PackageDecision(string Package, string Version, string? Line);
