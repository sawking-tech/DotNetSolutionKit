using System.Text.RegularExpressions;

namespace DotsKit.Solutions;

/// <summary>An error of a build: the file and the project as the build wrote them, full paths.</summary>
internal sealed record BuildError(string File, string Code, string Message, string Project);

/// <summary>The errors of a dotnet build, each once, under the project it is in: what a person looks at after a change.</summary>
internal static partial class BuildErrors
{
    private const string ProjectExtension = ".csproj";

    /// <summary>
    /// MSBuild's line: <c>file(line,col): error CODE: message [project]</c>. NuGet's names the project itself as
    /// the file, and the solution in the brackets.
    /// </summary>
    [GeneratedRegex(@"^\s*(?<file>.+?)(\(\d+,\d+\))?\s*: error (?<code>[A-Z]+\d+): (?<message>.*?)(\s+\[(?<project>[^\]]+)\])?\s*$")]
    private static partial Regex Error();

    /// <summary>The output as it is when it has no error line MSBuild writes - a restore or a tool that failed otherwise.</summary>
    public static IEnumerable<string> ByProject(string output)
    {
        var errors = Parse(output)
            .Select(e => (Project: NameOf(e.Project), Text: Describe(e)))
            .Distinct()
            .GroupBy(e => e.Project, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
        if (errors.Count == 0)
        {
            return [output.Trim()];
        }

        return errors.SelectMany(g => (IEnumerable<string>)[$"  {g.Key}", .. g.Select(e => $"    {e.Text}")]);
    }

    /// <summary>Each error of a build once: MSBuild writes an error again in its summary.</summary>
    public static IReadOnlyList<BuildError> Parse(string output) =>
        output.Split('\n')
            .Select(l => Error().Match(l))
            .Where(m => m.Success)
            .Select(m => new BuildError(m.Groups["file"].Value.Trim(), m.Groups["code"].Value, m.Groups["message"].Value, ProjectOf(m)))
            .Distinct()
            .ToList();

    /// <summary>The project in the brackets, or the file itself where it is the project, as NuGet writes it.</summary>
    private static string ProjectOf(Match error)
    {
        var bracket = error.Groups["project"].Value;
        // A project of several target frameworks is written as x.csproj::TargetFramework=net8.0.
        var project = bracket.Split("::")[0];
        return (project.EndsWith(ProjectExtension, StringComparison.OrdinalIgnoreCase) ? project : error.Groups["file"].Value).Trim();
    }

    private static string NameOf(string project) => Path.GetFileNameWithoutExtension(project.Replace('\\', '/'));

    /// <summary>The file without its folders, then the code and the message.</summary>
    private static string Describe(BuildError error)
    {
        var file = error.File.Replace('\\', '/');
        return $"{file[(file.LastIndexOf('/') + 1)..]}: {error.Code} {error.Message}";
    }
}
