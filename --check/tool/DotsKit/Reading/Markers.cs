namespace DotsKit.Reading;

/// <summary>
/// What each parameter of the template leaves in a solution, so a solution made without dotskit can be described:
/// a file or a folder only one value generates, or a section of a service's appsettings.json. Paths are relative
/// to the solution's root, <c>{P}</c> standing for its prefix (NamespaceRoot.ProductName) and <c>{S}</c> for a
/// service's folder. They hold for the template since 2.7, the first version that names itself in a solution;
/// whatever a marker misses, the check that generates the solution again shows.
/// </summary>
internal static class Markers
{
    public const string Prefix = "{P}";
    public const string Service = "{S}";

    /// <summary>The flags of the solution by what Common, the capabilities and the root have only with them.</summary>
    public static readonly IReadOnlyList<(string Flag, string Path)> SolutionFlags =
    [
        ("Storage", "src/common/{P}.Common.Infrastructure/Storage"),
        ("ClickHouse", "src/common/{P}.Common.Infrastructure/ClickHouse"),
        ("MongoDB", "src/capabilities/{P}.Capabilities.Mongo"),
        ("Infisical", "src/common/{P}.Common.Infrastructure/Configuration/Secrets/Infisical*"),
        ("Vault", "src/common/{P}.Common.Infrastructure/Configuration/Secrets/Vault*"),
        ("DiffApi", "src/common/{P}.Common.Web/Setup/SchemaDump.cs"),
        ("FeatureFlags", "src/common/{P}.Common/FeatureManagement"),
        ("HierarchyRules", "src/common/{P}.Common/Domain/IHierarchicalEntity.cs"),
        ("GitHubCiCd", ".github/workflows/ci.yml"),
    ];

    /// <summary>Audit leaves files only with the outbox, which is what the template turns it on with.</summary>
    public const string AuditPath = "src/common/{P}.Common.Application/Auditing";

    /// <summary>The email channel of --Notify, in the capability of its own since 2.8.</summary>
    public const string NotifyEmailPath = "src/capabilities/{P}.Capabilities.Notifications";

    public const string SqlServerPath = "src/common/{P}.Common.Infrastructure/Persistence/SqlServer";
    public const string ComposePath = "deploy/compose";
    public const string KubernetesPath = "deploy/k8s";
    public const string ClaudePath = ".claude";
    public const string OpenCodePath = ".opencode";
    public const string EnvExamplePath = "deploy/compose/.env.example";
    public const string HangfireEnvLine = "HANGFIRE_DASHBOARD_PASSWORD";
    public const string RabbitMqEnvLine = "RABBITMQ_";

    /// <summary>The script that names each image after the product in lower case with underscores: Retail.Shop -> retail_shop.</summary>
    public const string BuildImagesPath = "deploy/build-images.sh";

    /// <summary>
    /// Before 2.8 the template named the solution's first service in a comment of this script, and the check
    /// generates the solution with the same first service. Goes in 3.0 with the 2.7 base.
    /// </summary>
    public const string FirstServiceScript = "src/services/manual-add-projects.sh";

    /// <summary>Where the template writes its version into a solution, since 2.7.</summary>
    public const string BuildPropsPath = "Directory.Build.props";
    public const string VersionElement = "DotNetSolutionKitVersion";
    public const string ServiceVersionElement = "DotNetSolutionKitServiceVersion";

    public const string ApiFolder = "{S}/{S}.API";
    public const string ApiProject = "{S}/{S}.API/{S}.API.csproj";
    public const string AppSettings = "{S}/{S}.API/appsettings.json";
    public const string LaunchSettings = "{S}/{S}.API/Properties/launchSettings.json";
    public const string ProgramFile = "{S}/{S}.API/Program.cs";
    public const string ConfigurationFile = "{S}/{S}.API/Setup/ApplicationConfiguration.cs";
    public const string TestsProject = "{S}/{S}.Tests/{S}.Tests.csproj";

    /// <summary>A gateway is a service of its own kind, with routes and no domain.</summary>
    public const string GatewayFile = "{S}/{S}.API/Setup/GatewayTransforms.cs";
    public const string AuditGuardFile = "{S}/{S}.Tests/Tests/AuditGuardTests.cs";

    /// <summary>The flags of a service by the section its appsettings.json has only with them.</summary>
    public static readonly IReadOnlyList<(string Flag, string Section)> ServiceSections =
    [
        ("Storage", "S3"),
        ("ClickHouse", "ClickHouse"),
        ("MongoDB", "MongoDB"),
        ("Infisical", "Infisical"),
        ("Vault", "Vault"),
    ];

    public const string HangfireSection = "HangfireSettings";
    public const string BusSection = "RabbitMq";
    public const string EmailSection = "Email";

    /// <summary>Lines of a service's code only one value writes.</summary>
    public const string OutboxLine = "busNeedsDatabase: true";
    public const string DiffApiLine = "SchemaOnlyMode";
    public const string FeatureFlagsLine = "AddPlatformFeatures";
    public const string XunitLine = "xunit.v3";

    public static string Of(string path, string prefix, string? service = null) =>
        path.Replace(Prefix, prefix, StringComparison.Ordinal).Replace(Service, service ?? "", StringComparison.Ordinal);
}
