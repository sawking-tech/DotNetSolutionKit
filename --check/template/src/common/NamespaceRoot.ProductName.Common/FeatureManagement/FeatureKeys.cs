using JetBrains.Annotations;

namespace NamespaceRoot.ProductName.Common.FeatureManagement;

/// <summary>
/// Keys of the flags this platform's code reads.
/// </summary>
/// <remarks>
/// <para>
/// Flags themselves live in <c>features.json</c>, and a flag only the UI reacts to belongs there and
/// nowhere else — that is what lets one be added without touching code. A constant appears here when
/// backend code reads the flag, and it earns its place by giving autocomplete, find-usages and a
/// rename that is a refactor rather than a grep.
/// </para>
/// <para>
/// Generating this file from the JSON was considered and dropped: a source generator maintained
/// forever, to save writing a line, is a bad trade. A test keeps the two honest instead — every
/// constant here must exist in the file.
/// </para>
/// </remarks>
[PublicAPI]
public static class FeatureKeys
{
    /// <summary>
    /// Example entry, paired with the sample in <c>features.json</c>. Both go once the solution has
    /// real flags.
    /// </summary>
    public const string SampleFeature = "sample.feature";
}
