using System.Diagnostics;
using Hangfire;
using Hangfire.Client;
using Hangfire.Common;
using Hangfire.Server;
using Hangfire.Storage;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NamespaceRoot.ProductName.Common.Application.Tracing;
using NamespaceRoot.ProductName.Common.Domain.Context;
using NamespaceRoot.ProductName.Common.Infrastructure.Persistence.EntityFramework.Events;
using NamespaceRoot.ProductName.Common.Infrastructure.Security;
using NamespaceRoot.ProductName.Common.Tests.Stubs;

namespace NamespaceRoot.ProductName.Common.Tests.Persistence;

/// <summary>
/// A job runs on a Hangfire thread long after the request that enqueued it is gone. Whatever the
/// request knew about itself - who asked, which correlation identifier the caller searches by - has
/// to be written into the job at enqueue and put back around its execution, or the job's lines and
/// audit entries belong to nobody.
/// </summary>
[TestFixture]
public class JobActorPropagationTests
{
    private const string UserId = "11111111-1111-1111-1111-111111111111";

    [SetUp]
    public void Reset() => Activity.Current = null;

    [Test]
    public void The_correlation_of_the_request_is_back_while_the_job_runs()
    {
        Dictionary<string, object> parameters;
        using (Correlation.Use("client-chosen-42"))
            parameters = Enqueue(SystemUserContext.Instance);

        string? seen = null;
        Perform(parameters, () => seen = Correlation.Current);

        seen.ShouldBe("client-chosen-42", "the caller searching by its own identifier finds the job's lines too");
    }

    [Test]
    public void The_job_continues_the_trace_of_the_request()
    {
        using var request = new Activity("request").Start();
        var parameters = Enqueue(SystemUserContext.Instance);
        request.Stop();
        Activity.Current = null;

        string? trace = null;
        Perform(parameters, () => trace = Activity.Current?.TraceId.ToString());

        trace.ShouldBe(request.TraceId.ToString());
    }

    [Test]
    public void A_scheduled_job_is_correlated_by_a_trace_of_its_own()
    {
        var parameters = Enqueue(SystemUserContext.Instance);

        string? seen = null;
        Perform(parameters, () => seen = Correlation.Current);

        seen.ShouldNotBeNullOrEmpty("work with no identifier at all cannot be found in the log afterwards");
    }

    [Test]
    public void The_person_who_enqueued_is_restored_for_the_job()
    {
        var parameters = Enqueue(new UserContextMock(UserId, login: "operator@example.com"));

        IUserContext? seen = null;
        Perform(parameters, () => seen = JobActorContext.Actor);

        seen.ShouldNotBeNull();
        seen!.UserId.ShouldBe(Guid.Parse(UserId));
        seen.Login.ShouldBe("operator@example.com");
    }

    [Test]
    public void A_job_enqueued_by_the_system_names_nobody()
    {
        var parameters = Enqueue(SystemUserContext.Instance);

        IUserContext? seen = null;
        Perform(parameters, () => seen = JobActorContext.Actor);

        seen.ShouldBeNull("a job with no person behind it is the platform's own work, and says so");
    }

    [Test]
    public void Nothing_restored_outlives_the_job()
    {
        using (Correlation.Use("client-chosen-42"))
        {
            var parameters = Enqueue(new UserContextMock(UserId));
            Activity.Current = null;

            using (Correlation.Use("worker"))
            {
                Perform(parameters, () => { });

                Correlation.Current.ShouldBe("worker", "the next job on the same thread would inherit it");
                JobActorContext.Actor.ShouldBeNull();
                Activity.Current.ShouldBeNull();
            }
        }
    }

    // --- plumbing -----------------------------------------------------------------------------

    private static JobActorPropagationFilter Filter(IUserContext actor)
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => actor);
        return new JobActorPropagationFilter(services.BuildServiceProvider());
    }

    private static Dictionary<string, object> Enqueue(IUserContext actor)
    {
        var parameters = new Dictionary<string, object>();
        var create = new CreateContext(
            Mock.Of<JobStorage>(), Mock.Of<IStorageConnection>(), Job.FromExpression(() => Work()), null, parameters);

        Filter(actor).OnCreating(new CreatingContext(create));

        return parameters;
    }

    /// <summary>
    /// Runs the job the way the worker does: the parameters come back from storage serialized, the
    /// filter wraps the execution, and the handling sees what the filter restored.
    /// </summary>
    private static void Perform(Dictionary<string, object> parameters, Action onHandling)
    {
        var stored = parameters.ToDictionary(p => p.Key, p => SerializationHelper.Serialize(p.Value, SerializationOption.User));
        var connection = new Mock<IStorageConnection>();
        connection
            .Setup(c => c.GetJobParameter("job-1", It.IsAny<string>()))
            .Returns((string _, string name) => stored.GetValueOrDefault(name)!); // null for a missing one, as Hangfire's storages answer

        var job = new BackgroundJob("job-1", Job.FromExpression(() => Work()), DateTime.UtcNow, stored);
        var perform = new PerformContext(
            Mock.Of<JobStorage>(), connection.Object, job, Mock.Of<IJobCancellationToken>());

        // Not the test's actor: the worker has none, whatever enqueued the job.
        var filter = Filter(SystemUserContext.Instance);
        filter.OnPerforming(new PerformingContext(perform));
        onHandling();
        filter.OnPerformed(new PerformedContext(perform, null, false, null));
    }

    public static void Work() { }
}
