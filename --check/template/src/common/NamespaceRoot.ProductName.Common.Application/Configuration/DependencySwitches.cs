using Microsoft.Extensions.Configuration;

namespace NamespaceRoot.ProductName.Common.Application.Configuration;

/// <summary>
/// Which infrastructure a service uses on this run: <c>Database:Enabled</c>,
/// <c>HangfireSettings:Enabled</c> and <c>RabbitMq:Enabled</c>, each <c>true</c> unless configuration
/// says otherwise.
/// </summary>
/// <remarks>
/// A generation flag removes a part from the code for good; these switches leave the code in place and
/// stop registering it, so a team can generate a service with a database and a bus, run it without them
/// while there is nothing to store or send, and turn them on later with one setting. A switched-off
/// dependency registers no services, no options to validate and no readiness check.
///
/// They default to on: a deployed service that silently started without its database because a line of
/// configuration was missing would be worse than one that refuses to start.
/// </remarks>
public sealed class DependencySwitches
{
    public const string DatabaseKey = "Database:Enabled";
    public const string JobsKey = "HangfireSettings:Enabled";
    public const string BusKey = "RabbitMq:Enabled";

    private DependencySwitches(bool database, bool jobs, bool bus)
    {
        Database = database;
        Jobs = jobs;
        Bus = bus;
    }

    public bool Database { get; }

    public bool Jobs { get; }

    public bool Bus { get; }

    /// <summary>The configuration keys of the dependencies switched off, for the startup log.</summary>
    public IReadOnlyList<string> SwitchedOff =>
        new[] { (DatabaseKey, Database), (JobsKey, Jobs), (BusKey, Bus) }
            .Where(s => !s.Item2)
            .Select(s => s.Item1)
            .ToList();

    public static DependencySwitches Read(IConfiguration configuration) => new(
        configuration.GetValue(DatabaseKey, defaultValue: true),
        configuration.GetValue(JobsKey, defaultValue: true),
        configuration.GetValue(BusKey, defaultValue: true));
}

public static class DependencySwitchesExtensions
{
    /// <summary>
    /// Switches off what cannot run without the database once the database is off: the job server, whose
    /// storage is the database, and the bus when it delivers through the outbox in the database.
    /// </summary>
    /// <remarks>
    /// Written as configuration, added after every other source, so every reader of the switches - the
    /// registrations, the readiness checks, the pipeline - sees the same values.
    /// </remarks>
    public static IConfigurationBuilder AddDependencyOverrides(this IConfigurationBuilder builder, bool busNeedsDatabase)
    {
        if (DependencySwitches.Read(builder.Build()).Database)
            return builder;

        var overrides = new Dictionary<string, string?> { [DependencySwitches.JobsKey] = "false" };
        if (busNeedsDatabase)
            overrides[DependencySwitches.BusKey] = "false";

        return builder.AddInMemoryCollection(overrides);
    }
}
