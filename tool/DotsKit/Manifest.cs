using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotsKit.PackageUpdates;

namespace DotsKit;

/// <summary>
/// What a solution was generated from: the version of the template, the arguments of the solution - the
/// names, the database, the deployment and the flags Common and the root were generated with - and each
/// service with its own arguments. Kept in <c>.dotskit/manifest.json</c> and committed: it is how the tool
/// rebuilds what the template gave, the base of every merge. Which command made the solution is not kept:
/// a solution is its parts, whatever order they came in.
/// </summary>
internal sealed record Manifest
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Each field is explained in place by a _comment_<field> key, the notation of the template's appsettings.json:
    // written on every write, skipped on reading, since they have no setter.

    [SuppressMessage(Suppressions.Performance, "CA1822", Justification = Suppressions.JsonMetadataKey)]
    [JsonPropertyName("_comment_template")]
    public string TemplateComment => "The version of DotNetSolutionKit the solution is generated with; dotskit upgrade moves it to the tool's";

    public required string Template { get; init; }

    [SuppressMessage(Suppressions.Performance, "CA1822", Justification = Suppressions.JsonMetadataKey)]
    [JsonPropertyName("_comment_solution")]
    public string SolutionComment => "The template's arguments of Common and the root: the names, the database, the deployment, the flags the services share";

    /// <summary>The arguments Common and the root files are generated with, without a service's name.</summary>
    public required List<string> Solution { get; init; }

    [SuppressMessage(Suppressions.Performance, "CA1822", Justification = Suppressions.JsonMetadataKey)]
    [JsonPropertyName("_comment_services")]
    public string ServicesComment => "Each service by its own arguments; dotskit new adds one or joins flags to one";

    /// <summary>Each service by its arguments, its name (-S) among them.</summary>
    public required List<List<string>> Services { get; init; }

    [SuppressMessage(Suppressions.Performance, "CA1822", Justification = Suppressions.JsonMetadataKey)]
    [JsonPropertyName("_comment_packages")]
    public string PackagesComment => "How much of the template's package versions an upgrade brings";

    /// <summary>How much of the template's package versions the solution takes; the template's default when the manifest has none.</summary>
    public PackagePolicy Packages { get; init; } = new();

    [JsonPropertyName("_comment_upgrade")]
    public string? UpgradeComment => Upgrade is null ? null : "An upgrade made a part at a time (dotskit upgrade, unless --all-at-once): the release it goes to and the parts merged so far; the next dotskit upgrade takes the next part, and the last one moves template to that release";

    public UpgradeInProgress? Upgrade { get; init; }

    public static Manifest? Load(string root)
    {
        var path = Path.Combine(root, Layout.ManifestPath);
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
    }

    /// <summary>A manifest from its text, refused with the reason when the solution could not be rebuilt from it.</summary>
    public static Manifest Parse(string text)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(text, Json);
        var problem = manifest switch
        {
            null => "is empty",
            { Services.Count: 0 } => "lists no service",
            _ when !Generation.TemplateVersions.IsVersion(manifest.Template) => $"has no version of the template, but \"{manifest.Template}\"",
            _ when manifest.Services.Any(s => !TemplateArgs.Parse(s).HasService) => "has a service with no -S",
            _ when manifest.Packages is null => "has packages set to null",
            _ when manifest.Packages.Problem is { } packages => packages,
            _ => null,
        };
        return problem is null ? manifest! : throw new InvalidOperationException($"{Layout.ManifestPath} {problem}: put it back from git.");
    }

    /// <summary>The manifest as text, with LF line endings on every OS, so a team on two OSes writes the same file.</summary>
    public string Serialize() => JsonSerializer.Serialize(this, Json).ReplaceLineEndings("\n") + "\n";

    [JsonIgnore]
    public TemplateArgs SolutionArgs => TemplateArgs.Parse(Solution);

    [JsonIgnore]
    public IReadOnlyList<TemplateArgs> ServiceArgs => [.. Services.Select(TemplateArgs.Parse)];

    public TemplateArgs? FindService(string name) => ServiceArgs.FirstOrDefault(s => IsNamed(s, name));

    private static bool IsNamed(TemplateArgs service, string name) =>
        string.Equals(service.Service, name, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The manifest with a service added, or with the flags of a service it has joined to its own. The solution
    /// takes what the service brings that lives outside a service folder (<see cref="TemplateArgs.WidenBy"/>).
    /// </summary>
    public Manifest WithService(TemplateArgs service)
    {
        var existing = FindService(service.Service);
        // A service keeps the name it has: -S orders is Orders.
        var merged = existing is null ? service : existing.JoinedWith(service).With(TemplateArgs.ServiceName, existing.Service);
        var services = ServiceArgs.Select(s => IsNamed(s, service.Service) ? merged : s).ToList();
        if (existing is null)
        {
            services.Add(merged);
        }

        return this with
        {
            Solution = SolutionArgs.WidenBy(merged).InTemplateOrder().ToArgs(),
            Services = [.. services.Select(s => s.ToArgs())],
        };
    }

    /// <summary>
    /// The manifest with a service added and the solution left as it is: a service the template alone generated
    /// changed nothing outside its folder, so the base it was generated from has the solution unwidened.
    /// </summary>
    public Manifest WithServiceAsItIs(TemplateArgs service) => WithService(service) with { Solution = Solution };

    public Manifest WithTemplate(string version) => this with { Template = version };

    /// <summary>
    /// The upgrade a part at a time this solution is in, with <paramref name="part"/> merged; the last part ends it.
    /// <paramref name="parts"/> are the parts of the upgrade in their order, kept from its first run.
    /// </summary>
    public Manifest WithPartMerged(string to, string part, bool last, IReadOnlyList<string> parts) =>
        last
            ? this with { Template = to, Upgrade = null }
            : this with { Upgrade = new UpgradeInProgress { To = to, Parts = [.. parts], Done = [.. (Upgrade?.Done ?? []).Append(part).Distinct(StringComparer.Ordinal)] } };

    /// <summary>
    /// The parameters of the whole solution a command names with another value than the solution has, the
    /// template's default counting as a value: a service cannot change them, and dropping them would be silent.
    /// </summary>
    public IReadOnlyList<string> OtherSolutionValues(TemplateArgs command) =>
        [.. TemplateArgs.SolutionOnly.Where(n => command.Has(n)
            && !string.Equals(SolutionArgs[n], command[n], StringComparison.OrdinalIgnoreCase))];
}

/// <summary>An upgrade made a part at a time: where it goes and which parts are merged.</summary>
internal sealed record UpgradeInProgress
{
    [JsonPropertyName("_comment_to")]
    [SuppressMessage(Suppressions.Performance, "CA1822", Justification = Suppressions.JsonMetadataKey)]
    public string ToComment => "The release the upgrade goes to";

    public required string To { get; init; }

    [JsonPropertyName("_comment_done")]
    [SuppressMessage(Suppressions.Performance, "CA1822", Justification = Suppressions.JsonMetadataKey)]
    public string DoneComment => "The parts merged so far: shared - the root and deploy - then common and each project of src/common, capabilities, then each service folder";

    public required List<string> Done { get; init; }

    [JsonPropertyName("_comment_parts")]
    [SuppressMessage(Suppressions.Performance, "CA1822", Justification = Suppressions.JsonMetadataKey)]
    public string PartsComment => "Every part of the upgrade in its order, taken at its first run from the solution and the template's next output: a project the template adds is a part of its own";

    public List<string>? Parts { get; init; }
}
