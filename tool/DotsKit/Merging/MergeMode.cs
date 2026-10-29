namespace DotsKit.Merging;

/// <summary>How the template's output reaches a solution that is there already.</summary>
/// <param name="ByGit">
/// By git: the output is committed to the branch dotskit/template and merged into the working branch, and a
/// conflict is resolved in the IDE (<see cref="GitTemplateMerge"/>). Otherwise the tool's own three-way merge
/// writes the files (<see cref="ThreeWayMerge"/>).
/// </param>
/// <param name="PreferTemplate">--force: in a conflict the template's side is taken; the solution's lines stay in git history.</param>
internal sealed record MergeMode(bool ByGit, bool PreferTemplate)
{
    public const string Git = "git";
    public const string Files = "files";
}
