using NamespaceRoot.ProductName.Common.Application.Messaging;
using NamespaceRoot.ProductName.Common.Domain.Messaging;

namespace NamespaceRoot.ProductName.Common.Tests.Stubs;

/// <summary>
/// A bus that keeps what was published instead of delivering it.
/// </summary>
/// <remarks>
/// Used where the assertion is about what a service told the rest of the platform, rather than about
/// what a consumer then did with it. Publishing is recorded in order, so a test can also say that one
/// message went out and not two.
/// </remarks>
public sealed class RecordingMessageBus : IMessageBus
{
    private readonly List<object> _published = [];
    private readonly List<object> _sent = [];

    /// <summary>Everything published, oldest first.</summary>
    public IReadOnlyList<object> Published => _published;

    /// <summary>Everything sent to a named endpoint, oldest first.</summary>
    public IReadOnlyList<object> Sent => _sent;

    /// <summary>The published messages of one kind.</summary>
    public IReadOnlyList<T> PublishedOf<T>() => _published.OfType<T>().ToList();

    public Task PublishAsync<T>(T busEvent, CancellationToken ct = default)
        where T : class, IBusEvent
    {
        _published.Add(busEvent);

        return Task.CompletedTask;
    }

    public Task SendAsync<T>(T command, CancellationToken ct = default)
        where T : class, IBusCommand
    {
        _sent.Add(command);

        return Task.CompletedTask;
    }
}
