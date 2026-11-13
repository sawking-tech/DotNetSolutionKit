# Persistence (MongoDB)

`Capabilities.Persistence.Mongo` gives a service a MongoDB it can read and write beside its
relational database: the client, the database, the typed options and a health check.

```csharp
services.AddMongoDB(configuration);
...
await _mongoStore.Collection<Order>("orders").InsertOneAsync(order);
```

### What is inside

- `DependencyInjection.AddMongoDB` - reads `Mongo` from configuration, registers the client and
  `IMongoStore`.
- `IMongoStore` / `MongoStore` - the `IMongoDatabase` the service works with, resolved from the
  configured connection.
- `MongoOptions` - the connection string and the database name, validated at startup.
- `MongoHealthCheck` - the readiness check that the database answers.

What is deliberately not here: the documents and the queries - a collection name, a model and its
indexes are the service's own.

---

Part of [DotNetSolutionKit](https://dotskit.sawking.tech/), MIT License, Copyright (c) 2025 Vladimir Savkin.
