// Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace NamespaceRoot.ProductName.Common.Infrastructure.Http.Internal;

public static class InternalServiceHttpExtensions
{
    /// <summary>
    /// Makes a client a call to another service of the product: the internal key, the caller and the
    /// correlation identifier go with every request.
    /// </summary>
    /// <remarks>
    /// Works on any client the factory builds, typed or Refit:
    /// <code>
    /// services.AddRefitClient&lt;IBillingClient&gt;()
    ///     .ConfigureHttpClient(c => c.BaseAddress = new Uri(options.BaseUrl))
    ///     .AddInternalServiceHandlers();
    /// </code>
    /// For a provider outside the product use <see cref="AddCorrelation"/> alone: the key and the
    /// user's details are not theirs to see.
    /// </remarks>
    public static IHttpClientBuilder AddInternalServiceHandlers(this IHttpClientBuilder builder)
    {
        builder.Services.TryAddTransient<InternalApiKeyDelegatingHandler>();
        builder.Services.TryAddTransient<UserContextDelegatingHandler>();

        return builder
            .AddHttpMessageHandler<InternalApiKeyDelegatingHandler>()
            .AddHttpMessageHandler<UserContextDelegatingHandler>()
            .AddCorrelation();
    }

    /// <summary>
    /// Puts the correlation identifier of the running work on every request of the client.
    /// </summary>
    public static IHttpClientBuilder AddCorrelation(this IHttpClientBuilder builder)
    {
        builder.Services.TryAddTransient<CorrelationDelegatingHandler>();
        return builder.AddHttpMessageHandler<CorrelationDelegatingHandler>();
    }
}
