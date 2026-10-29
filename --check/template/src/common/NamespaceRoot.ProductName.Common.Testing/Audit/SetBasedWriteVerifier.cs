using System.Text.RegularExpressions;
using Shouldly;

namespace NamespaceRoot.ProductName.Common.Tests.Audit;

/// <summary>
/// Fails when a repository writes past the change tracker without saying whether the audit journal
/// is fed in its place.
/// </summary>
/// <remarks>
/// <para>
/// Works on source rather than on metadata deliberately: the thing being detected is a call inside a
/// method body, which reflection cannot see. Reading the file is what makes the check possible at
/// all, and it costs one regex per repository.
/// </para>
/// <para>
/// This exists because the marking guard on entities is not enough. A balance entity can carry
/// <c>[Auditable]</c> and produce not one entry, because its balance moves through
/// <c>ExecuteUpdateAsync</c>, as counters of limits do. The attribute is silent about that, and so is
/// the journal.
/// </para>
/// </remarks>
public static class SetBasedWriteVerifier
{
    private static readonly Regex SetBasedCall = new(
        @"\.(ExecuteUpdateAsync|ExecuteDeleteAsync|ExecuteSqlRawAsync|ExecuteSqlInterpolatedAsync)\s*\(",
        RegexOptions.Compiled);

    private static readonly Regex MethodDeclaration = new(
        @"^\s*(?:\[[^\]]*\]\s*)*(?:public|internal|protected|private)\s[^;=]*\b(\w+)\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// Scans the repositories of one service and requires each method containing a set-based write
    /// to carry <c>[SetBasedWrite]</c>.
    /// </summary>
    /// <param name="serviceFolderName">
    /// Folder under <c>src/services</c>, e.g. <c>NamespaceRoot.ProductName.Orders</c>.
    /// </param>
    public static void VerifyService(string serviceFolderName)
        => VerifyRepositories(Path.Combine(FindSourceRoot(), "services", serviceFolderName));

    /// <summary>
    /// Walks up from the test binaries until the repository's <c>src</c> directory appears.
    /// </summary>
    /// <remarks>
    /// The check reads source, so it needs the tree rather than the assemblies. Failing loudly when
    /// it cannot be found matters — a guard that silently scans nothing passes forever.
    /// </remarks>
    private static string FindSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src");
            if (Directory.Exists(candidate)) return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException(
            $"No 'src' directory above {AppContext.BaseDirectory} — the set-based write guard cannot read sources.");
    }

    /// <summary>
    /// Scans every <c>*Repository.cs</c> under <paramref name="sourceRoot"/> and requires each method
    /// containing a set-based write to carry <c>[SetBasedWrite]</c>.
    /// </summary>
    /// <param name="sourceRoot">Directory holding the service's source tree.</param>
    public static void VerifyRepositories(string sourceRoot)
    {
        var root = new DirectoryInfo(sourceRoot);
        root.Exists.ShouldBeTrue($"Source root '{sourceRoot}' not found — the guard would pass vacuously.");

        var offenders = new List<string>();

        foreach (var file in root.EnumerateFiles("*Repository.cs", SearchOption.AllDirectories))
        {
            if (file.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                continue;

            offenders.AddRange(FindUnmarkedWrites(file));
        }

        offenders.ShouldBeEmpty(
            "These methods write past the change tracker, so the audit interceptor cannot see them, "
            + "and they do not say what happens to the journal: "
            + string.Join("; ", offenders)
            + ". Mark each with [SetBasedWrite(audited: true, \"...\")] and record the change through "
            + "IAuditRecorder, or [SetBasedWrite(audited: false, \"...\")] when it carries no operator "
            + "decision.");
    }

    private static IEnumerable<string> FindUnmarkedWrites(FileInfo file)
    {
        var lines = File.ReadAllLines(file.FullName);
        var currentMethod = string.Empty;
        var currentMethodMarked = false;
        var reported = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            if (MethodDeclaration.Match(line) is { Success: true } declaration)
            {
                currentMethod = declaration.Groups[1].Value;
                currentMethodMarked = IsMarked(lines, i);
            }

            if (!SetBasedCall.IsMatch(line) || currentMethodMarked || currentMethod.Length == 0)
                continue;

            if (reported.Add(currentMethod))
                yield return $"{file.Name}.{currentMethod}";
        }
    }

    /// <summary>Looks back over the attributes attached to the declaration at <paramref name="index"/>.</summary>
    private static bool IsMarked(IReadOnlyList<string> lines, int index)
    {
        for (var i = index; i >= 0 && i >= index - 12; i--)
        {
            var line = lines[i].TrimStart();

            if (line.StartsWith("[SetBasedWrite", StringComparison.Ordinal)) return true;

            // Stop at the previous member: attributes never span past a closing brace.
            if (i < index && (line.StartsWith('}') || line.EndsWith(';'))) return false;
        }

        return false;
    }
}
