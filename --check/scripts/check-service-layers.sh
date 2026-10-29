#!/usr/bin/env bash
#
# Writes the first lines a team writes into a generated service, and builds and tests them:
#
#   scripts/check-service-layers.sh <solution> <service>   # <service>: the -S of a generated service, e.g. Orders
#
# A generated service holds no business code, so a layer that cannot see what its first use case needs
# builds green until someone writes that use case. This writes one into a copy of the solution: a use case in
# the application layer (an exception and the execution context from Common, a specification, a response
# from Common.Contracts), a consumer in the infrastructure when the solution has a bus (the message from
# Common.Contracts), and a test of the use case. The copy is built and the test run; the solution is left
# as it was.
set -euo pipefail

solution="$(cd "$1" && pwd)" service="$2"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
cp -r "$solution/." "$work"
cd "$work"

sln=$(ls src/services/*.All.sln)
root=$(basename "$sln" .All.sln)
dir="src/services/$root.$service"
[ -d "$dir" ] || { echo "::error::no service $root.$service in $solution"; exit 2; }
contracts="src/common/$root.Common.Contracts"

mkdir -p "$contracts/LayerProbe" "$dir/$root.$service.Application/LayerProbe" "$dir/$root.$service.Tests/Tests/LayerProbe"

cat > "$contracts/LayerProbe/LayerProbeResponse.cs" <<CS
namespace $root.Common.Contracts.LayerProbe;

/// <summary>What the probe's use case answers.</summary>
public sealed record LayerProbeResponse(string Name, Guid Actor);
CS

cat > "$dir/$root.$service.Application/LayerProbe/LayerProbeService.cs" <<CS
using LinqSpecs;
using $root.Common.Contracts.LayerProbe;
using $root.Common.Domain.Context;
using $root.Common.Domain.Specifications;
using $root.Common.Exceptions;

namespace $root.$service.Application.LayerProbe;

/// <summary>A use case as the skills write one: a contract answer, an exception, the actor, a query.</summary>
public sealed class LayerProbeService(IDomainExecutionContext context)
{
    public LayerProbeResponse Get(string? name) =>
        string.IsNullOrEmpty(name)
            ? throw new NotFoundException("No name")
            : new LayerProbeResponse(name, context.Actor.UserId);

    public QuerySpecification<LayerProbeResponse> Named(string name) =>
        new(new AdHocSpecification<LayerProbeResponse>(r => r.Name == name));
}
CS

if [ -f "src/common/$root.Common.Infrastructure/Messaging/Consumers/BusEventConsumer.cs" ]; then
    mkdir -p "$contracts/Messaging/LayerProbe" "$dir/$root.$service.Infrastructure/LayerProbe"
    cat > "$contracts/Messaging/LayerProbe/LayerProbeHappenedV1.cs" <<CS
using $root.Common.Domain.Messaging;

namespace $root.Common.Contracts.Messaging.LayerProbe;

/// <summary>The probe's message.</summary>
public sealed record LayerProbeHappenedV1 : IBusEvent
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public DateTimeOffset OccurredOnUtc { get; init; } = DateTimeOffset.UtcNow;
}
CS
    cat > "$dir/$root.$service.Infrastructure/LayerProbe/LayerProbeConsumer.cs" <<CS
using Microsoft.Extensions.Logging;
using $root.Common.Application.Messaging.Consumers;
using $root.Common.Contracts.Messaging.LayerProbe;
using $root.Common.Infrastructure.Messaging.Consumers;

namespace $root.$service.Infrastructure.LayerProbe;

/// <summary>A consumer where the messaging guide puts it: in the service's infrastructure.</summary>
public sealed class LayerProbeConsumer(ILogger<LayerProbeConsumer> logger) : BusEventConsumer<LayerProbeHappenedV1>(logger)
{
    protected override Task HandleAsync(IMessageContext<LayerProbeHappenedV1> context) => Task.CompletedTask;
}
CS
    echo "with a consumer"
fi

cat > "$dir/$root.$service.Tests/Tests/LayerProbe/LayerProbeTests.cs" <<CS
using $root.Common.Contracts.LayerProbe;
using $root.Common.Domain.Context;
using $root.Common.Exceptions;
using $root.Common.Tests;
using $root.Common.Tests.Stubs;
using $root.$service.Application.LayerProbe;

namespace $root.$service.Tests.Tests.LayerProbe;

public class LayerProbeTests
{
    private static ServiceTestExecutionContext<LayerProbeService> Context()
    {
        var context = new ServiceTestExecutionContext<LayerProbeService>();
        context.Register<IDomainExecutionContext, TestDomainExecutionContext>(
            new TestDomainExecutionContext(new UserContextMock(), TimeProvider.System));
        return context;
    }

    [Test(Description = "probe")]
    public async Task Answers_With_The_Contract()
    {
        await using var ctx = Context();
        ctx.Act(s => s.Get("probe")).ShouldBe(new LayerProbeResponse("probe", Guid.Parse(TestDomainExecutionContext.DefaultTestUserId)));
    }

    [Test(Description = "probe")]
    public async Task Refuses_With_The_Common_Exception()
    {
        await using var ctx = Context();
        Should.Throw<NotFoundException>(() => ctx.Act(s => s.Get(null)));
    }
}
CS

dotnet test "$dir/$root.$service.sln" --filter "FullyQualifiedName~LayerProbe" --nologo
echo "The first use case, consumer and test of $root.$service build and pass without touching a project file."
