namespace DotsKit;

/// <summary>
/// The arguments of a generation as named parameters of the template. Short names (<c>-S</c>, <c>-I</c>) and
/// long ones in any case (<c>--Storage</c>, <c>--storage</c>, <c>--api-gateway</c>) map to the parameter's own
/// name, so two commands compare by meaning.
/// </summary>
internal sealed class TemplateArgs
{
    private const string On = "true";
    public const string ServiceName = "ServiceNameOrCustom";
    public const string HttpPort = "HttpPort";
    private const string Solution = "Solution";
    private const string Minimal = "Minimal";
    private const string Messaging = "Messaging";
    private const string Notify = "Notify";
    private const string ApiGateway = "ApiGateway";

    private static readonly Dictionary<string, string> ShortNames = new(StringComparer.Ordinal)
    {
        ["-N"] = "NamespaceRoot",
        ["-P"] = "ProductName",
        ["-S"] = ServiceName,
        ["-M"] = Minimal,
        ["-H"] = "Hangfire",
        ["-I"] = "Infisical",
        ["-FF"] = "FeatureFlags",
        ["-CH"] = "ClickHouse",
        ["-A"] = ApiGateway,
        ["-Ag"] = "Agent",
    };

    /// <summary>The parameters of the template this version of the tool is released with.</summary>
    private static readonly string[] Parameters =
    [
        "NamespaceRoot", "ProductName", ServiceName, Solution, Minimal, "Hangfire", Messaging,
        ApiGateway, "Storage", "ClickHouse", "MongoDB", Notify, "Audit", "TestFramework", "Agent", "Deploy",
        "Infisical", "Database", "Vault", "HttpPort", "DiffApi", "GitHubCiCd", "FeatureFlags", "HierarchyRules",
    ];

    /// <summary>One for the whole solution: a service takes them from the solution, a command need not name them.</summary>
    public static readonly string[] SolutionOnly =
        ["NamespaceRoot", "ProductName", "Database", "Deploy", "TestFramework", "Agent", "GitHubCiCd"];

    /// <summary>
    /// The bool flags with a part outside a service folder - in Common, the root, deploy - or, as Hangfire, a
    /// block in a file of the root (.env.example): the solution has one when any of its services has it.
    /// </summary>
    private static readonly string[] WidenedFlags =
        ["Infisical", "Vault", "DiffApi", "FeatureFlags", "HierarchyRules", "Storage", "ClickHouse", "MongoDB", "Audit", "Hangfire"];

    /// <summary>What says which command made the solution; a manifest keeps neither.</summary>
    private static readonly string[] Mode = [Solution, Minimal];

    /// <summary>The bus of a solution is the strongest of its services': none &lt; direct &lt; outbox.</summary>
    private static readonly string[] MessagingOrder = ["none", "direct", "outbox"];

    /// <summary>
    /// The template's defaults that are not off or empty: a parameter a command does not name has this value.
    /// Hangfire is on by default, so a service generated without -H has jobs, and the solution needs their part.
    /// </summary>
    private static readonly Dictionary<string, string> Defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Hangfire"] = On,
        [Messaging] = "none",
        ["Database"] = "postgres",
        ["Deploy"] = "compose",
        ["TestFramework"] = "nunit",
        ["Agent"] = "claude",
        ["GitHubCiCd"] = "false",
    };

    /// <summary>The parameters that are not bool: given without a value, they would take "true" for a name.</summary>
    private static readonly HashSet<string> WithValue = new(StringComparer.OrdinalIgnoreCase)
    {
        "NamespaceRoot", "ProductName", ServiceName, HttpPort, Messaging, Notify, "Database", "Deploy", "TestFramework", "Agent",
    };

    private readonly List<KeyValuePair<string, string>> _values;

    private TemplateArgs(List<KeyValuePair<string, string>> values) => _values = values;

    public static TemplateArgs Empty { get; } = new([]);

    public static TemplateArgs Parse(IReadOnlyList<string> args)
    {
        var values = new List<KeyValuePair<string, string>>();
        for (var i = 0; i < args.Count; i++)
        {
            var name = Canonical(args[i]) ?? throw new ArgumentException($"Not a parameter of the template: {args[i]}");
            // A flag with no value is a bool switched on; --Solution is written that way.
            var hasValue = i + 1 < args.Count && !args[i + 1].StartsWith('-');
            if (!hasValue && WithValue.Contains(name))
                throw new ArgumentException($"{args[i]} needs a value.");
            values.Add(new(name, hasValue ? args[++i] : On));
        }
        return new TemplateArgs(values);
    }

    /// <summary>
    /// The template's own name of a parameter, whatever form it was given in. dotnet new tells case apart; the
    /// tool does not, so a command keeps working when the template moves its names to lower case.
    /// </summary>
    private static string? Canonical(string arg)
    {
        if (ShortNames.TryGetValue(arg, out var name))
            return name;
        if (!arg.StartsWith("--", StringComparison.Ordinal) || arg.Length <= 2)
            return null;
        var key = arg[2..].Replace("-", "", StringComparison.Ordinal);
        return Parameters.FirstOrDefault(p => string.Equals(p, key, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>The value a generation with these arguments has: the one given, or the template's default.</summary>
    public string? this[string name] => _values.LastOrDefault(v => Is(v.Key, name)).Value ?? Defaults.GetValueOrDefault(name);

    public bool Has(string name) => _values.Any(v => Is(v.Key, name));

    public bool IsOn(string name) => string.Equals(this[name], On, StringComparison.OrdinalIgnoreCase);

    public string Service => this[ServiceName] ?? throw new ArgumentException("A service is named with -S.");

    public bool HasService => Has(ServiceName);

    /// <summary>A command that asks for the solution: <c>--Solution</c>, or <c>-M false</c> of earlier versions.</summary>
    public bool AsksForSolution => IsOn(Solution) || string.Equals(this[Minimal], "false", StringComparison.OrdinalIgnoreCase);

    public int? Port => int.TryParse(this[HttpPort], out var port) ? port : null;

    /// <summary>
    /// The arguments with <paramref name="name"/> set to <paramref name="value"/>: in its place when it is there,
    /// at the end when it is new. Keeping the place keeps a manifest the same when the same command runs again.
    /// </summary>
    public TemplateArgs With(string name, string value)
    {
        var at = _values.FindIndex(v => Is(v.Key, name));
        if (at < 0)
            return new([.. _values, new(name, value)]);
        var values = Without(name)._values;
        values.Insert(at, new(_values[at].Key, value));
        return new(values);
    }

    public TemplateArgs Without(params string[] names) => new([.. _values.Where(v => !names.Any(n => Is(v.Key, n)))]);

    /// <summary>These arguments, and those of <paramref name="other"/> over them where both name a parameter.</summary>
    public TemplateArgs JoinedWith(TemplateArgs other) =>
        other._values.Aggregate(this, (args, v) => args.With(v.Key, v.Value));

    /// <summary>What of a command belongs to the solution: everything but the service's name, its port and the mode.</summary>
    public TemplateArgs SolutionPart() => Without([ServiceName, HttpPort, ApiGateway, .. Mode]);

    /// <summary>What of a command belongs to the service: everything but what is the solution's and the mode.</summary>
    public TemplateArgs ServicePart() => Without([.. SolutionOnly, .. Mode]);

    /// <summary>
    /// The solution with what a service brings outside its folder: its widened flags, its channels of
    /// <c>--Notify</c>, and its bus where it is stronger than the solution's.
    /// </summary>
    public TemplateArgs WidenBy(TemplateArgs service)
    {
        var solution = WidenedFlags.Where(service.IsOn).Aggregate(this, (args, flag) => args.With(flag, On));
        var channels = Channels(this).Union(Channels(service), StringComparer.OrdinalIgnoreCase).ToList();
        if (channels.Count > 0)
            solution = solution.With(Notify, string.Join(',', channels));
        return Rank(service[Messaging]) > Rank(solution[Messaging]) ? solution.With(Messaging, service[Messaging]!) : solution;
    }

    private static IEnumerable<string> Channels(TemplateArgs args) =>
        (args[Notify] ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static int Rank(string? messaging) =>
        Array.FindIndex(MessagingOrder, m => string.Equals(m, messaging, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The command that generates the solution - Common, the root, All.sln - with this service as its first, in
    /// the words of the version that runs it: <c>--Solution</c>, or <c>-M false</c> before 2.8
    /// (<see cref="Generation.TemplateVersions.HasSolutionFlag"/>, which says when that branch goes).
    /// </summary>
    public static TemplateArgs ForSolution(TemplateArgs solution, TemplateArgs firstService, string version) =>
        Generation.TemplateVersions.HasSolutionFlag(version)
            ? solution.With(ServiceName, firstService.Service).With(Solution, On)
            : solution.With(ServiceName, firstService.Service).With(Minimal, "false");

    /// <summary>
    /// The command that generates a service folder into a solution: the solution's own parameters, then the
    /// service's. No mode: a service folder is what every version generates by default.
    /// </summary>
    public static TemplateArgs ForService(TemplateArgs solution, TemplateArgs service) =>
        solution.Without([.. WidenedFlags, Notify, Messaging]).JoinedWith(service.Without(Mode));

    /// <summary>The arguments the template of <paramref name="version"/> has a parameter for (<see cref="Generation.TemplateVersions.HasParameter"/>).</summary>
    public TemplateArgs ForVersion(string version) => new([.. _values.Where(v => Generation.TemplateVersions.HasParameter(version, v.Key))]);

    /// <summary>
    /// The arguments in the order the template declares its parameters: the solution's arguments are the same
    /// whatever order its services came in.
    /// </summary>
    public TemplateArgs InTemplateOrder() =>
        new([.. _values.OrderBy(v => Array.FindIndex(Parameters, p => Is(p, v.Key)))]);

    /// <summary>The arguments as stored and as passed to dotnet new: every parameter by its long name.</summary>
    public List<string> ToArgs() => [.. _values.SelectMany(v => new[] { "--" + v.Key, v.Value })];

    private static bool Is(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
