using System.Globalization;
using DotsKit.Reading;

namespace DotsKit.Commands;

/// <summary>A release init checked a solution against: how much it reproduced, or why it could not be generated.</summary>
internal sealed record ReleaseCheck(string Version, Reproduction? Check, string? Failure = null);

/// <summary>
/// Which release reproduces a solution best, of those init checked, and the table that shows it: the share of the
/// template's files the solution has as they are. A tie goes to the newer release, which leaves less to upgrade; a
/// release that could not be generated is listed with its reason and takes no part.
/// </summary>
internal static class VersionAdvice
{
    /// <summary>The closest release; null when none could be generated.</summary>
    public static string? Closest(IReadOnlyList<ReleaseCheck> checks) =>
        checks.Where(c => c.Check is not null)
            .OrderByDescending(c => Share(c.Check!)).ThenByDescending(c => Version.Parse(c.Version))
            .Select(c => c.Version).FirstOrDefault();

    public static IEnumerable<string> Lines(IReadOnlyList<ReleaseCheck> checks) =>
        checks.Select(c => c.Check is { } check
            ? string.Create(CultureInfo.InvariantCulture,
                $"  {c.Version}: {check.Reproduced} of {check.Total} files of the template as they are ({Math.Round(Share(check) * 100)}%)")
            : $"  {c.Version}: not generated - {c.Failure}");

    private static double Share(Reproduction check) => check.Total == 0 ? 0 : (double)check.Reproduced / check.Total;
}
