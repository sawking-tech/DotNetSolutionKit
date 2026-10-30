using DotsKit.Merging;
using DotsKit.Solutions;

namespace DotsKit.Planning;

/// <summary>What a command will do to a solution, shown before anything is written.</summary>
/// <param name="Root">The solution's root, where the changes go.</param>
/// <param name="Next">The manifest the solution will have.</param>
/// <param name="Changes">The files, All.sln aside: it changes by its projects, in <paramref name="Membership"/>.</param>
/// <param name="Membership">The projects All.sln gains and loses.</param>
/// <param name="Packages">What changes in the package versions and what the solution's policy holds, a line each.</param>
/// <param name="Types">Where the types of the template live in the next version and where the ones it moved went; null for a new solution.</param>
/// <param name="PreferTemplate">--force: git takes the template's side in a conflict.</param>
/// <param name="ByGit">
/// The two outputs git merges into the solution (<see cref="GitTemplateMerge"/>); <paramref name="Changes"/> then names the
/// template's own changes. Null when the tool writes <paramref name="Changes"/> itself.
/// </param>
/// <param name="OnConflicts">
/// The manifest written instead of <paramref name="Next"/> when the merge leaves conflicts: the last part of an upgrade a
/// part at a time keeps the upgrade open, so the run after the resolution repairs the usings, builds and moves the version.
/// </param>
/// <param name="Parts">
/// Every part of an upgrade a part at a time in its order, from the solution and the template's next output together.
/// </param>
/// <param name="BaseTree">The template's output the solution came from, of this step.</param>
/// <param name="NextTree">The template's next output the solution is merged with: the change of its references from the base is the template's.</param>
/// <param name="Waiting">
/// The template's files of the parts an upgrade a part at a time has not merged yet, relative to the root: the repair
/// by the build's errors leaves them to their part.
/// </param>
internal sealed record Plan(string Root, Manifest Next, IReadOnlyList<Change> Changes, MembershipChange Membership, IReadOnlyList<string>? Packages = null,
    TypeMap? Types = null, (TemplateCommit Base, TemplateCommit Next)? ByGit = null, bool PreferTemplate = false, Manifest? OnConflicts = null,
    IReadOnlyList<string>? Parts = null, IReadOnlySet<string>? Waiting = null, string? BaseTree = null, string? NextTree = null)
{
    /// <summary>The manifest the solution has once the plan is written, by whether the merge left conflicts.</summary>
    public Manifest Written(bool conflicts) => conflicts && OnConflicts is { } open ? open : Next;
}
