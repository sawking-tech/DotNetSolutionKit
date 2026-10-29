using NamespaceRoot.ProductName.Common.Contracts;
using NamespaceRoot.ProductName.Common.Domain.Messaging;

namespace NamespaceRoot.ProductName.Common.Tests.Tests.Messaging;

/// <summary>
/// Where the bus looks for the commands it has to know an address for.
/// </summary>
/// <remarks>
/// Sending a command whose address was never registered fails at send time, not at start-up, and only
/// with a broker actually running — so the failure surfaces only where a broker is up. The
/// registration scans an assembly, and this fixture holds the line on which assembly that is.
/// </remarks>
[TestFixture]
[Parallelizable(ParallelScope.All)]
internal class CommandAddressRegistrationTests
{
    [Test(Description = "Commands live in the contracts assembly, which is the one the registration scans")]
    public void Should_FindCommands_When_ScanningTheContractsAssembly()
    {
        var commands = typeof(ContractsMarker).Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false }
                           && typeof(IBusCommand).IsAssignableFrom(type))
            .ToList();

        // The registration scans this assembly, and finding nothing here leaves every command
        // unaddressed. A freshly generated solution declares no commands yet, so until the first one
        // exists the result is inconclusive rather than a failure.
        Assume.That(commands, Is.Not.Empty,
            "no command is declared yet; declare commands in Common.Contracts, which the registration scans");
    }

    [Test(Description = "The assembly that declares the interfaces carries no commands of its own")]
    public void Should_FindNothing_When_ScanningTheAssemblyThatDeclaresTheInterface()
    {
        var commands = typeof(IBusCommand).Assembly
            .GetTypes()
            .Where(type => type is { IsAbstract: false, IsInterface: false }
                           && typeof(IBusCommand).IsAssignableFrom(type))
            .ToList();

        commands.ShouldBeEmpty(
            "the abstractions sit in the domain, so scanning by the interface's assembly is what used to find nothing");
    }
}
