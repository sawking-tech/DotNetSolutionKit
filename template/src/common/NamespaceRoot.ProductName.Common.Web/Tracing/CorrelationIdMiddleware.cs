// Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.

using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Serilog.Context;
using NamespaceRoot.ProductName.Common.Application.Tracing;

namespace NamespaceRoot.ProductName.Common.Web.Tracing;

/// <summary>
/// Gives every request a correlation identifier, puts it and the trace identifiers on the log, and returns
/// it to the caller.
/// </summary>
/// <remarks>
/// Correlation answers "is this the same story"; the trace and span identifiers, added by the Serilog span
/// enricher, answer "what called what". Both are needed: a log filtered by correlation alone shows the
/// lines of one request from several services interleaved, with no way to tell which call caused which.
///
/// Runs first in the pipeline — a request that fails in authentication still has to be findable.
/// </remarks>
public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ReadOrCreate(context);
        context.Items[TracingProperties.CorrelationId] = correlationId;

        var activity = Activity.Current;
        activity?.SetBaggage(TracingHeaders.CorrelationId, correlationId);

        // Echoed on the way out, including for responses written by later middleware, so it has to be set
        // before anything can start writing the body.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[TracingHeaders.CorrelationId] = correlationId;
            return Task.CompletedTask;
        });

        // Only the correlation id is pushed here. Trace, span and parent ids come from the Serilog span
        // enricher, which reads the same Activity and keeps working outside a request — in a job or a bus
        // consumer, where this middleware never runs. Pushing them here as well would leave two sources
        // for one property and no way to tell which one a given line came from.
        using (Correlation.Use(correlationId))
        using (LogContext.PushProperty(TracingProperties.CorrelationId, correlationId))
        {
            await _next(context);
        }
    }

    /// <summary>
    /// Takes the caller's identifier when there is one, so a chain that started elsewhere stays one chain.
    /// Falls back to the trace identifier, which every request has once tracing is on, and only then to a
    /// fresh value.
    /// </summary>
    private static string ReadOrCreate(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(TracingHeaders.CorrelationId, out var supplied))
        {
            var value = Sanitise(supplied.ToString());
            if (value.Length > 0)
            {
                return value;
            }
        }

        var traceId = Activity.Current?.TraceId.ToString();
        return string.IsNullOrEmpty(traceId) ? Guid.NewGuid().ToString("n") : traceId;
    }

    /// <summary>
    /// The value reaches the log and the response header, so line breaks (log forging, header splitting)
    /// are dropped, and the length is bounded — the header comes from outside.
    /// </summary>
    private static string Sanitise(string value)
    {
        var cleaned = value.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
        return cleaned.Length <= 128 ? cleaned : cleaned[..128];
    }
}
