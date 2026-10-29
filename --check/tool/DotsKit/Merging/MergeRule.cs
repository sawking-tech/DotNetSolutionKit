namespace DotsKit.Merging;

/// <summary>
/// What a file needs, from its three versions: the base (what the template gave before), the new (what it gives
/// now) and the solution's. Pure: it decides, and leaves the merging of two changed texts to whoever asks.
/// </summary>
internal static class MergeRule
{
    /// <summary>The decision: a change, nothing (null), or both texts changed and a three-way merge is needed.</summary>
    internal abstract record Decision;

    internal sealed record Final(Change? Change) : Decision;

    internal sealed record NeedsMerge(FileText Solution, FileText Base, FileText Template, string Reason) : Decision;

    public static Decision Decide(string path, FileText? @base, FileText? template, FileText? solution)
    {
        if (template is not null && solution is not null && template.SameTextAs(solution))
        {
            return new Final(null);
        }

        if (@base is null)
        {
            return Added(path, template, solution);
        }

        if (template is null)
        {
            return Removed(path, @base, solution);
        }

        if (template.SameTextAs(@base))
        {
            return new Final(null);
        }

        if (solution is null)
        {
            return new Final(new(path, ChangeKind.Kept, null, "changed in the template, removed from the solution: left removed"));
        }

        if (solution.SameTextAs(@base))
        {
            return new Final(new(path, ChangeKind.Update, KeepEndings(template, solution), "changed in the template only"));
        }

        return new NeedsMerge(solution, @base, template, "changed in the solution and in the template");
    }

    private static Decision Added(string path, FileText? template, FileText? solution) =>
        template is null ? new Final(null)
        : solution is null ? new Final(new(path, ChangeKind.Add, template, "new in the template"))
        // Both added a file by this path: with no common base, the empty file stands for it.
        : new NeedsMerge(solution, new FileText("", solution.Bom), template, "added by the solution and by the template");

    private static Final Removed(string path, FileText @base, FileText? solution) =>
        solution is null ? new Final(null)
        : solution.SameTextAs(@base) ? new Final(new(path, ChangeKind.Delete, null, "removed from the template"))
        : new Final(new(path, ChangeKind.Kept, null, "removed from the template, changed in the solution: kept"));

    /// <summary>The template's text with the line endings the solution's copy has.</summary>
    public static FileText KeepEndings(FileText template, FileText solution) =>
        template.IsBinary || solution.IsBinary ? template : template with { Text = LineEndings.Apply(template.Text, LineEndings.Of(solution.Text)) };
}
