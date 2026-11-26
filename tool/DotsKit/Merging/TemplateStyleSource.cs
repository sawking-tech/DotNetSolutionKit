namespace DotsKit.Merging;

/// <summary>
/// The template's style, as the package hands it to a solution: the file the package carries, embedded into the tool's
/// own assembly. The merge of an upgrade reads it to lay the template's side out (<see cref="StyleUnion"/>), so the code
/// the merge brings arrives in the team's style.
/// </summary>
internal static class TemplateStyleSource
{
    private const string Resource = "DotsKit.Style.editorconfig";

    /// <summary>The template's .editorconfig; the file is in the package and in the tool, one source for both.</summary>
    public static string Text()
    {
        using var stream = typeof(TemplateStyleSource).Assembly.GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException($"{Resource} is not in the tool's assembly: the build embeds template/.editorconfig.template as DotsKit.Style.editorconfig.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
