namespace DotsKit.Solutions;

/// <param name="Add">Projects the template added since the base and the solution's All.sln does not have; a project of the base the team took out stays out.</param>
/// <param name="Remove">Projects the template had and has no more, still in the solution's All.sln; a project the solution added itself is never removed.</param>
internal sealed record MembershipChange(IReadOnlyList<string> Add, IReadOnlyList<string> Remove)
{
    public bool IsEmpty => Add.Count == 0 && Remove.Count == 0;

    /// <summary>The three-way merge of All.sln by its projects, the way a file is merged by its lines.</summary>
    public static MembershipChange Of(IReadOnlySet<string> solution, IReadOnlySet<string> @base, IReadOnlySet<string> next) =>
        new(
            [.. next.Where(p => !@base.Contains(p) && !solution.Contains(p)).Order(StringComparer.Ordinal)],
            [.. @base.Where(p => !next.Contains(p) && solution.Contains(p)).Order(StringComparer.Ordinal)]);
}
