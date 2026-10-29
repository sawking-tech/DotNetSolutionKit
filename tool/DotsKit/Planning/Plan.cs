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
internal sealed record Plan(string Root, Manifest Next, IReadOnlyList<Change> Changes, MembershipChange Membership, IReadOnlyList<string>? Packages = null,
    TypeMap? Types = null, (TemplateCommit Base, TemplateCommit Next)? ByGit = null, bool PreferTemplate = false);
