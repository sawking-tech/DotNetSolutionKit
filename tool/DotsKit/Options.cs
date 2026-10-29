namespace DotsKit;

/// <summary>The tool's own options; every other argument goes to the template as it is.</summary>
internal sealed class Options
{
    public const string Usage = """
        dotskit - makes a solution with DotNetSolutionKit, adds services and flags to it and upgrades it, keeping your changes.

          dotskit new -N MyCompany -P MyProduct -S Orders   in a folder with no solution: make one with this service
          dotskit new -S Billing --Storage true             in a solution: add a service; it takes the solution's names
          dotskit new -S Orders --MongoDB true              in a solution: add a flag to a service it has
          dotskit init                                      in a solution made without dotskit: describe it in its manifest
          dotskit upgrade                                   in a solution: bring it to this version of the template
          dotskit upgrade --to 2.3.0                        in a solution: bring it to an earlier release, a step at a time

        Every command shows what it will change and asks before writing.
          --yes                     write without asking (CI)
          --force                   in a conflict take the template's side; your lines stay in git history
          --no-build                do not build the solution after writing
          --allow-dirty             run on a working tree with uncommitted changes
          --template-version 2.x.y  init: the version a solution was made by, where it does not say
          --to 2.x.y                upgrade: the release to go to, not newer than the tool
        """;

    private const string YesOption = "--yes";
    private const string ForceOption = "--force";
    private const string NoBuildOption = "--no-build";
    private const string AllowDirtyOption = "--allow-dirty";
    private const string TemplateVersionOption = "--template-version";
    private const string ToOption = "--to";

    public bool Yes { get; private init; }
    public bool Force { get; private init; }
    public bool NoBuild { get; private init; }
    public bool AllowDirty { get; private init; }
    public string? TemplateVersion { get; private init; }
    public string? To { get; private init; }
    public List<string> TemplateArgs { get; private init; } = [];

    /// <summary>The options of <paramref name="command"/>, refusing one that belongs to another command.</summary>
    public static Options Parse(string command, IReadOnlyList<string> args)
    {
        var options = Parse(args);
        if (options.TemplateVersion is not null && command != "init")
            throw new ArgumentException($"{TemplateVersionOption} is an option of dotskit init.");
        if (options.To is not null && command != "upgrade")
            throw new ArgumentException($"{ToOption} is an option of dotskit upgrade.");
        return options;
    }

    public static Options Parse(IReadOnlyList<string> args)
    {
        string[] flags = [YesOption, ForceOption, NoBuildOption, AllowDirtyOption];
        var (templateVersion, afterVersion) = Valued(args, TemplateVersionOption);
        var (to, rest) = Valued(afterVersion, ToOption);
        return new Options
        {
            Yes = rest.Any(a => Is(a, YesOption)),
            Force = rest.Any(a => Is(a, ForceOption)),
            NoBuild = rest.Any(a => Is(a, NoBuildOption)),
            AllowDirty = rest.Any(a => Is(a, AllowDirtyOption)),
            TemplateVersion = templateVersion,
            To = to,
            TemplateArgs = [.. rest.Where(a => !flags.Any(f => Is(a, f)))],
        };
    }

    /// <summary>The value of an option that takes one, and the arguments without the two.</summary>
    private static (string? Value, IReadOnlyList<string> Others) Valued(IReadOnlyList<string> args, string option)
    {
        var at = args.ToList().FindIndex(a => Is(a, option));
        if (at < 0)
            return (null, args);
        if (at + 1 >= args.Count)
            throw new ArgumentException($"{option} needs a version: {option} 2.7.0.");
        return (args[at + 1], [.. args.Take(at), .. args.Skip(at + 2)]);
    }

    private static bool Is(string arg, string option) => string.Equals(arg, option, StringComparison.OrdinalIgnoreCase);
}
