namespace DotsKit.Merging;

/// <summary>How the template's output reaches a solution that is there already; a merge by git is the only way.</summary>
/// <param name="PreferTemplate">--force: in a conflict the template's side is taken; the solution's lines stay in git history.</param>
/// <param name="MarkBase">--conflict-style zdiff3: a conflict is marked with its base, as zdiff3 writes it; plain is the default.</param>
/// <param name="NoStyle">
/// The run keeps the bytes as they are: the template's side is not laid out by the team's style before the merge. A
/// tool run for a comparison, a rehearsal, or a machine with no dotnet format gets the merge of 2.9 back.
/// </param>
internal sealed record MergeMode(bool PreferTemplate, bool MarkBase, bool NoStyle = false);
