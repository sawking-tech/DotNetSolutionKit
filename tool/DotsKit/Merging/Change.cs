namespace DotsKit.Merging;

internal enum ChangeKind
{
    /// <summary>The template adds a file the solution does not have.</summary>
    Add,
    /// <summary>The solution did not change the file; it takes the template's new one.</summary>
    Update,
    /// <summary>The template removed a file the solution did not change.</summary>
    Delete,
}

/// <param name="Path">The file, relative to the solution's root, with forward slashes.</param>
/// <param name="Kind">What happens to the file.</param>
/// <param name="Content">What the file becomes; null for a deletion.</param>
/// <param name="Reason">Why, as the report says it.</param>
internal sealed record Change(string Path, ChangeKind Kind, FileText? Content, string Reason);
