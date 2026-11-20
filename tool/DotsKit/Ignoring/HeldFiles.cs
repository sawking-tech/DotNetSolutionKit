namespace DotsKit.Ignoring;

/// <summary>
/// The paths <c>.dotskitignore</c> holds, put out of the template's reach before the merge: each of them takes the
/// text the solution came from, and one the template adds and the base does not have is taken out of the template's
/// output. The merge by git then finds nothing of the template there, so the team's file stands untouched, whatever the
/// template changed in it or around it.
/// </summary>
internal static class HeldFiles
{
    /// <summary>
    /// <paramref name="nextDir"/> with the held paths as <paramref name="baseDir"/> has them; a file the base does not
    /// have leaves the next output, since the template adding it is the template touching it.
    /// </summary>
    /// <param name="nextDir">The template's next output, written in place.</param>
    /// <param name="baseDir">The template's output the solution came from; null for a new solution, where nothing is held.</param>
    /// <param name="held">The paths the rules hold, relative to the tree.</param>
    public static void Apply(string nextDir, string? baseDir, IReadOnlySet<string> held)
    {
        if (baseDir is null || held.Count == 0)
        {
            return;
        }

        foreach (var path in held)
        {
            var next = Path.Combine(nextDir, path);
            var before = Path.Combine(baseDir, path);
            if (File.Exists(before))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(next)!);
                File.Copy(before, next, overwrite: true);
            }
            else if (File.Exists(next))
            {
                File.Delete(next);
            }
        }
    }
}
