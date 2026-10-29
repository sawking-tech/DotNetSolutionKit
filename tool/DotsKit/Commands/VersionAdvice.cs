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
    /// <summary>The major version from which a solution is upgraded by the tool; one of an earlier major is brought to it by hand.</summary>
    private const int FirstMajorWithTheTool = 2;

    private const string FirstRelease = "2.0.0";

    /// <summary>The closest release; null when none could be generated.</summary>
    public static string? Closest(IReadOnlyList<ReleaseCheck> checks) =>
        checks.Where(c => c.Check is not null)
            .OrderByDescending(c => Share(c.Check!)).ThenByDescending(c => Version.Parse(c.Version))
            .Select(c => c.Version).FirstOrDefault();

    /// <summary>
    /// What init asks a person to do with the closest release. A 2.x is named as it is. A 1.x differs from 2.0.0 in most
    /// files, and a merge of the two leaves most of them in conflict, so the solution is brought to 2.0.0 by the list of
    /// upgrading.md first and described as 2.0.0; dotskit upgrade takes it on from there.
    /// </summary>
    public static string Advice(string closest) =>
        Version.Parse(closest).Major < FirstMajorWithTheTool
            ? $"Nothing is written: the closest by the files above is {closest}, before the breaking changes of {FirstRelease}. Bring the code to {FirstRelease} by docs/getting-started/upgrading.md, \"A solution of v1\", then describe it, dotskit init --template-version {FirstRelease}."
            : $"Nothing is written: name the version, dotskit init --template-version {closest}, the closest by the files above.";

    public static IEnumerable<string> Lines(IReadOnlyList<ReleaseCheck> checks) =>
        checks.Select(c => c.Check is { } check
            ? string.Create(CultureInfo.InvariantCulture,
                $"  {c.Version}: {check.Reproduced} of {check.Total} files of the template as they are ({Math.Round(Share(check) * 100)}%)")
            : $"  {c.Version}: not generated - {c.Failure}");

    private static double Share(Reproduction check) => check.Total == 0 ? 0 : (double)check.Reproduced / check.Total;
}
