using ClickHouse.Client.ADO;
using Microsoft.Extensions.Options;
using NamespaceRoot.ProductName.Common.Exceptions;

namespace NamespaceRoot.ProductName.Common.Infrastructure.ClickHouse;

/// <summary>
/// Opens ClickHouse connections over one shared HTTP client, so sockets are pooled across connections.
/// </summary>
public interface IClickHouseConnections
{
    /// <summary>A new connection; a <see cref="ServiceUnavailableException"/> when ClickHouse is switched off.</summary>
    ClickHouseConnection Create();

    /// <summary>The per-command timeout from settings.</summary>
    int CommandTimeoutSeconds { get; }
}

internal sealed class ClickHouseConnections(IOptions<ClickHouseOptions> options, IHttpClientFactory httpClients)
    : IClickHouseConnections
{
    public const string HttpClientName = "clickhouse";

    public int CommandTimeoutSeconds => options.Value.CommandTimeoutSeconds;

    public ClickHouseConnection Create()
    {
        if (!options.Value.Enabled)
            throw new ServiceUnavailableException("ClickHouse is switched off (ClickHouse:Enabled=false).");

        return new ClickHouseConnection(options.Value.ConnectionString, httpClients.CreateClient(HttpClientName));
    }
}
