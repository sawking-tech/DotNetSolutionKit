using System.Text.RegularExpressions;

namespace DotsKit.Ignoring;

/// <summary>
/// The version a rule of <c>.dotskitignore</c> holds to, written after the path: <c>path@2.9</c>, <c>path@2</c> or
/// <c>path@major</c>. A pin holds a file against the upgrades below its precision and lets it merge at one above:
/// <c>@2.9</c> holds against the patches of 2.9, <c>@2</c> against its minors. <c>@major</c> holds against every
/// upgrade of the current major; a new major spends it, and the tool moves it onto the next line, commented out, for
/// the team to confirm.
/// </summary>
internal sealed partial record Pin(int Major, int? Minor, bool EveryMajor)
{
    /// <summary>The pin written after a path, or null for a suffix that is no pin.</summary>
    public static Pin? Parse(string suffix) =>
        suffix switch
        {
            "major" => new Pin(0, null, EveryMajor: true),
            { } text when Digits().IsMatch(text) => ParseNumbers(text),
            _ => null,
        };

    private static Pin? ParseNumbers(string text) =>
        text.Split('.') switch
        {
            [var major] when int.TryParse(major, out var only) => new Pin(only, null, EveryMajor: false),
            [var major, var minor] when int.TryParse(major, out var a) && int.TryParse(minor, out var b) => new Pin(a, b, EveryMajor: false),
            _ => null,
        };

    /// <summary>
    /// Whether the pin still holds the file at <paramref name="version"/>: an upgrade that stays below the pin's line
    /// leaves the file alone, and the upgrade that moves past it spends the pin and the file goes into a normal merge.
    /// <c>@2.9</c> holds every 2.9.x and stops at 2.10; <c>@2</c> holds every 2.x and stops at 3.0.
    /// </summary>
    public bool Holds(string version)
    {
        if (EveryMajor)
        {
            return true;
        }

        return Version.TryParse(version.Split('-')[0], out var target)
            && (Minor is { } minor ? (target.Major, target.Minor).CompareTo((Major, minor)) : target.Major.CompareTo(Major)) <= 0;
    }

    /// <summary>
    /// The pin at the same precision, renewed to <paramref name="version"/>: <c>@2.9</c> renewed to 3.1.4 is <c>@3.1</c>,
    /// <c>@2</c> to <c>@3</c>. <c>@major</c> keeps its word.
    /// </summary>
    public string RenewedTo(string version) =>
        EveryMajor || !Version.TryParse(version.Split('-')[0], out var target)
            ? "major"
            : Minor is not null ? $"{target.Major}.{target.Minor}" : target.Major.ToString();

    /// <summary>Letters and digits of a version, never a path: a pin goes into a comparison alone.</summary>
    [GeneratedRegex(@"^\d{1,9}(\.\d{1,9})?\z")]
    private static partial Regex Digits();
}
