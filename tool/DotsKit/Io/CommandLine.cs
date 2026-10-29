namespace DotsKit.Io;

/// <summary>
/// A list of paths may not fit one command line: Windows starts a process with at most 32 767 characters of it, and a
/// solution of a few hundred files passes that. A program that reads its paths from a file is given them that way
/// (<see cref="GitClient"/>); one that does not, as <c>dotnet sln</c>, runs again on a batch at a time, each batch a
/// command line that fits. The batches run one after another: each writes the same file.
/// </summary>
internal static class CommandLine
{
    /// <summary>Well under the 32 767 characters of Windows, which also count the program's path and its arguments' quotes.</summary>
    public const int MaxLength = 24_000;

    /// <summary>A space and the quotes around an argument, as the runner passes it.</summary>
    private const int ArgumentOverhead = 3;

    /// <summary>
    /// The items in batches, each with the arguments around it no longer than <paramref name="maxLength"/>; an item
    /// too long for a batch of its own is refused before the first batch of this list.
    /// </summary>
    /// <param name="items">The paths, in the order they go in.</param>
    /// <param name="fixedArguments">The arguments every batch carries besides its items.</param>
    /// <param name="maxLength">The longest command line a batch may make.</param>
    public static IEnumerable<IReadOnlyList<string>> Batches(IEnumerable<string> items, IReadOnlyCollection<string> fixedArguments, int maxLength = MaxLength)
    {
        var fixedLength = fixedArguments.Sum(Length);
        var all = items.ToList();
        if (all.FirstOrDefault(item => fixedLength + Length(item) > maxLength) is { } tooLong)
        {
            throw new InvalidOperationException(
                $"{tooLong} alone makes a command line of {fixedLength + Length(tooLong)} characters, past the {maxLength} dotskit allows for one command: give the solution or the project a shorter path.");
        }

        var batch = new List<string>();
        var length = fixedLength;
        foreach (var item in all)
        {
            if (batch.Count > 0 && length + Length(item) > maxLength)
            {
                yield return batch;
                (batch, length) = ([], fixedLength);
            }

            batch.Add(item);
            length += Length(item);
        }

        if (batch.Count > 0)
        {
            yield return batch;
        }
    }

    private static int Length(string argument) => argument.Length + ArgumentOverhead;
}
