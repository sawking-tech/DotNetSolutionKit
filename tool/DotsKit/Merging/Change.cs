namespace DotsKit.Merging;

internal enum ChangeKind
{
    /// <summary>The template adds a file the solution does not have.</summary>
    Add,
    /// <summary>The solution did not change the file; it takes the template's new one.</summary>
    Update,
    /// <summary>Both changed the file, in different places; the merge keeps both.</summary>
    Merge,
    /// <summary>The template removed a file the solution did not change.</summary>
    Delete,
    /// <summary>One side removed what the other changed, or both changed a binary file: the solution's file stays as it is, and the case is reported.</summary>
    Kept,
    /// <summary>Both changed the same lines: the file is written with both sides marked, for a person to resolve.</summary>
    Conflict,
}

/// <param name="Path">The file, relative to the solution's root, with forward slashes.</param>
/// <param name="Kind">What happens to the file.</param>
/// <param name="Content">What the file becomes; null for a deletion and for a file kept as it is.</param>
/// <param name="Reason">Why, as the report says it.</param>
internal sealed record Change(string Path, ChangeKind Kind, FileText? Content, string Reason);
