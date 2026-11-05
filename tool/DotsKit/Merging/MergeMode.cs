namespace DotsKit.Merging;

/// <summary>How the template's output reaches a solution that is there already; a merge by git is the only way.</summary>
/// <param name="PreferTemplate">--force: in a conflict the template's side is taken; the solution's lines stay in git history.</param>
/// <param name="MarkBase">--conflict-style zdiff3: a conflict is marked with its base, as zdiff3 writes it; plain is the default.</param>
internal sealed record MergeMode(bool PreferTemplate, bool MarkBase);
