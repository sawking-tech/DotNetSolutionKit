using DotsKit.Generation;

namespace DotsKit.Commands;

/// <summary>
/// Where a team reads what it may have to change by hand between two versions: the sections of upgrading.md for
/// the versions crossed. The page on GitHub, not the site: the site renders the docs by a script, and a reader that
/// takes the page as text, an agent among them, gets none of it. A release reads the page on master, which keeps a
/// section written after the release; a pre-release and the template's sources read it on dev, where their
/// section is.
/// </summary>
internal static class UpgradeGuide
{
    private const string Page = "/docs/getting-started/upgrading.md";
    private const string Released = Repository.Url + "/blob/master" + Page;
    private const string Developed = Repository.Url + "/blob/dev" + Page;

    public static string Of(string from, string to) =>
        $"What a solution may have to change by hand, from {Minor(from)} to {Minor(to)}: the sections for these versions of {(IsRelease(to) ? Released : Developed)}";

    private static bool IsRelease(string version) => version != TemplateSource.SourcesVersion && !version.Contains('-');

    private static string Minor(string version) =>
        Version.TryParse(version.Split('-')[0], out var parsed) ? $"v{parsed.Major}.{parsed.Minor}" : version;
}
