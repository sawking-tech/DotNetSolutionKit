using NamespaceRoot.ProductName.Capabilities.Notifications.Adapters;

namespace NamespaceRoot.ProductName.Capabilities.Notifications.Factories;

internal interface IGraphClientFactory
{
    IGraphClientAdapter Create();
}

internal sealed class GraphClientFactory : IGraphClientFactory
{
    private readonly INotificationEmailSettings _settings;

    public GraphClientFactory(INotificationEmailSettings settings)
    {
        _settings = settings;
    }

    public IGraphClientAdapter Create()
    {
        var graph = _settings.GraphApi ?? throw new InvalidOperationException("GraphApi configuration is missing.");
        return new GraphClientAdapter(graph.TenantId, graph.ClientId, graph.ClientSecret);
    }
}
