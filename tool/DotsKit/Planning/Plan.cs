using DotsKit.Merging;
using DotsKit.Solutions;

namespace DotsKit.Planning;

/// <summary>What a command will do to a solution, shown before anything is written.</summary>
/// <param name="Root">The solution's root, where the changes go.</param>
/// <param name="Next">The manifest the solution will have.</param>
/// <param name="Changes">The files, All.sln aside: it changes by its projects, in <paramref name="Membership"/>.</param>
/// <param name="Packages">What changes in the package versions and what the solution's policy holds, a line each.</param>
/// <param name="Types">Where the types of the template live in the next version and where the ones it moved went; null for a new solution.</param>
internal sealed record Plan(string Root, Manifest Next, IReadOnlyList<Change> Changes, MembershipChange Membership, IReadOnlyList<string>? Packages = null,
    TypeMap? Types = null)
{
    public bool HasConflicts => Changes.Any(c => c.Kind == ChangeKind.Conflict);
}
