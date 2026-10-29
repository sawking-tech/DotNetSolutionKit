using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace NamespaceRoot.ProductName.Common.FeatureManagement;

/// <summary>
/// A feature as it stands right now: what was declared about it, what it currently evaluates to, and
/// where that value came from.
/// </summary>
/// <remarks>
/// The source is not decoration. Configuration is layered — the shared file, then the environment,
/// then whatever external store is wired up — so an operator can turn a flag off in one place and see
/// nothing change because a later layer overrides it. Reporting the winning layer is what turns that
/// from a mystery into a fact on screen.
/// </remarks>
[PublicAPI]
public sealed record FeatureState
{
    /// <summary>What was declared about the feature.</summary>
    public required FeatureDescriptor Descriptor { get; init; }

    /// <summary>Whether the feature is on, after every configuration layer has had its say.</summary>
    public required bool Enabled { get; init; }

    /// <summary>Which layer decided <see cref="Enabled"/>.</summary>
    public required FeatureValueSource Source { get; init; }

    /// <summary>Whether the flag has outlived the date its author gave it.</summary>
    public bool Expired { get; init; }

    /// <summary>Convenience for callers that only ever need the key.</summary>
    public string Key => Descriptor.Key;
}

/// <summary>The configuration layer a feature's current value came from.</summary>
/// <remarks>
/// Serialized by name. A management UI, and anyone reading a response by hand, needs "File" rather
/// than 1 — and a number would also silently change meaning if a member were ever inserted.
/// </remarks>
[PublicAPI]
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum FeatureValueSource
{
    /// <summary>Nothing said otherwise, so the declared default stands.</summary>
    Default = 0,

    /// <summary>The shared feature file — the platform's own answer, and the fallback when a store is unreachable.</summary>
    File = 1,

    /// <summary>An environment-specific entry in the file, matched against the running environment.</summary>
    Environment = 2,

    /// <summary>An external configuration or secrets store layered over the file.</summary>
    Store = 3,

    /// <summary>An environment variable, which outranks the file and is easy to forget having set.</summary>
    EnvironmentVariable = 4,

    /// <summary>The shared file, pinned there over every other layer.</summary>
    Pinned = 5,
}
