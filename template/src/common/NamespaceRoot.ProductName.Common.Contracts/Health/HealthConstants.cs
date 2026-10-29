namespace NamespaceRoot.ProductName.Common.Contracts.Health;

public static class HealthConstants
{
    /// <summary>Liveness: the process answers. Checks no dependency.</summary>
    public const string Health = "/health";

    /// <summary>Readiness: every dependency tagged <see cref="ReadyTag"/> answers.</summary>
    public const string Ready = "/ready";

    /// <summary>Tag of the health checks <see cref="Ready"/> runs.</summary>
    public const string ReadyTag = "ready";

    public static readonly string[] AllPaths = [Health, Ready];
}
