using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using NamespaceRoot.ProductName.Common.Exceptions;

namespace NamespaceRoot.ProductName.Capabilities.Mongo;

/// <summary>
/// The service's MongoDB database. Readers and writers stay in the service, behind its own ports, and
/// take their collections from here.
/// </summary>
public interface IMongoStore
{
    /// <summary>The service's database; a <see cref="ServiceUnavailableException"/> when MongoDB is switched off.</summary>
    IMongoDatabase Database { get; }

    /// <summary>A collection of the service's database.</summary>
    IMongoCollection<TDocument> Collection<TDocument>(string name);
}

internal sealed class MongoStore(IOptions<MongoOptions> options, IServiceProvider services) : IMongoStore
{
    public IMongoDatabase Database
    {
        get
        {
            if (!options.Value.Enabled)
                throw new ServiceUnavailableException("MongoDB is switched off (MongoDB:Enabled=false).");

            // Resolved here, not in the constructor: with MongoDB switched off no client is ever built.
            return services.GetRequiredService<IMongoClient>().GetDatabase(options.Value.Database);
        }
    }

    public IMongoCollection<TDocument> Collection<TDocument>(string name) => Database.GetCollection<TDocument>(name);
}
