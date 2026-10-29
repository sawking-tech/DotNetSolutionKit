namespace DotsKit;

/// <summary>Why the code turns a rule of .editorconfig off at a place: each reason named once, for its attribute.</summary>
internal static class Suppressions
{
    public const string Performance = "Performance";
    public const string Design = "Design";

    /// <summary>CA1822 on a key of ADR-006: System.Text.Json writes instance properties only.</summary>
    public const string JsonMetadataKey = "System.Text.Json writes instance properties only, and the _comment_ key of ADR-006 is in every file written.";

    /// <summary>CA1001 on a holder of a SemaphoreSlim that lives as long as the process.</summary>
    public const string NothingToRelease = "A SemaphoreSlim never asked for its AvailableWaitHandle holds no unmanaged resource, and its holder lives as long as the process.";
}
