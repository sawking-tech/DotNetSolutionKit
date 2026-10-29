using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace NamespaceRoot.ProductName.Common.FeatureManagement;

/// <summary>
/// Everything declared about one feature flag: what it is, who owns it, where it applies and when it
/// stops being justified.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately not called a definition — <c>Microsoft.FeatureManagement</c> has a
/// <c>FeatureDefinition</c> of its own, and two nearly identical names in the same file would be a
/// lasting source of confusion. This is the platform's description of a flag; the library's type is
/// what we hand it to evaluate.
/// </para>
/// <para>
/// A descriptor is data, not code. Flags live in a configuration file every service reads, so adding
/// one that only the UI reacts to needs no deployment. A flag that switches behaviour still needs the
/// branch it switches, and that branch is what makes it worth a named constant beside it.
/// </para>
/// </remarks>
[PublicAPI]
public sealed record FeatureDescriptor
{
    /// <summary>
    /// Published name, in kebab-case, dotted for a sub-feature: <c>credit-monitoring</c>,
    /// <c>credit-monitoring.predictive</c>. Renaming one breaks whoever asks for it.
    /// </summary>
    public required string Key { get; init; }

    /// <summary>Value used where no environment overrides it. Defaults to off.</summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Whether the value in the shared file wins over every other layer: an external store and
    /// environment variables.
    /// </summary>
    /// <remarks>
    /// For a flag whose value in the store must not apply here for a while: the file is edited, the flag
    /// pinned, and the store is left as it is until the pin is taken off.
    /// </remarks>
    public bool Pinned { get; init; }

    /// <summary>
    /// What turning the flag on actually does. Write flags so that on means the feature works;
    /// <see cref="FeatureEffect.Disables"/> exists for the kill switch that could not be phrased the
    /// other way round, and its <see cref="Description"/> is expected to say why.
    /// </summary>
    public FeatureEffect Effect { get; init; } = FeatureEffect.Enables;

    /// <summary>
    /// Per-environment overrides, keyed by environment name — <c>Development</c>, <c>Staging</c>,
    /// <c>Production</c>. Matched case-insensitively against the running environment.
    /// </summary>
    /// <remarks>
    /// This is what lets one shared file serve every stand. Without it a single file would force the
    /// same answer everywhere, which is the opposite of what a flag is for.
    /// </remarks>
    public IReadOnlyDictionary<string, bool> Environments { get; init; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Markers grouping flags into one feature. A capability spanning several services shows up as
    /// several keys; a shared tag is what lets a reader — or a management UI — see them as one thing.
    /// </summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>One sentence on what the flag turns on. It is all a reader of the list has to go on.</summary>
    public string? Description { get; init; }

    /// <summary>
    /// Who answers for this flag. The field that matters when someone has to decide, at an
    /// inconvenient hour, whether flipping it is safe.
    /// </summary>
    public string? Owner { get; init; }

    /// <summary>
    /// The date after which the flag is technical debt rather than a decision. Reported as expired
    /// once it passes; a temporary flag without one stays forever.
    /// </summary>
    public DateOnly? ExpiresAt { get; init; }

    /// <summary>
    /// Work item the flag came from. Named for the thing rather than for the tracker, so moving off
    /// one does not leave a field lying about what it holds.
    /// </summary>
    public string? Ticket { get; init; }

    /// <summary>
    /// Whether the flag has outlived its stated date, as of <paramref name="today"/>.
    /// </summary>
    public bool IsExpired(DateOnly today) => ExpiresAt is { } expiry && today > expiry;

    /// <summary>
    /// The value this descriptor gives in <paramref name="environment"/>, before anything outside the
    /// file overrides it.
    /// </summary>
    public bool ValueIn(string? environment) =>
        environment is not null && Environments.TryGetValue(environment, out var overridden)
            ? overridden
            : Enabled;
}

/// <summary>What switching a flag on does to the feature behind it.</summary>
/// <remarks>
/// Serialized by name. A management UI, and anyone reading a response by hand, needs "File" rather
/// than 1 — and a number would also silently change meaning if a member were ever inserted.
/// </remarks>
[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FeatureEffect
{
    /// <summary>On means the feature works. The way flags should be written.</summary>
    Enables = 0,

    /// <summary>On means the feature is withheld — a kill switch. The exception, and it owes an explanation.</summary>
    Disables = 1,
}
