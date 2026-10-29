namespace NamespaceRoot.ProductName.Common.Tests;

/// <summary>
/// Skips the running test, in whatever way the test framework does it.
/// </summary>
/// <remarks>
/// An integration test with no server to run against is skipped with that reason rather than failed, so a
/// plain <c>dotnet test</c> needs no database. Skipping is the framework's to do - NUnit ignores, xUnit
/// skips - and this project has no framework, so each test project sets <see cref="Handler"/> once, from a
/// module initializer. Without one the test fails with the reason, which is never silently green.
/// </remarks>
public static class TestSkip
{
    /// <summary>What skipping means in the test framework of the running test project.</summary>
    public static Action<string> Handler { get; set; } = reason => throw new InvalidOperationException(
        $"Skipped: {reason} (set TestSkip.Handler in the test project to report it as a skip)");

    /// <summary>Skips the running test because of <paramref name="reason"/>.</summary>
    public static void Because(string reason) => Handler(reason);
}
