using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application.Abstractions;
using Serilog.Context;

namespace Recuro.BuildingBlocks.Web.Middleware;

/// <summary>
/// X-Request-ID and X-Correlation-ID on every request, response, log line, span and published event
/// (RCU-BKD-001 §1.2 "Correlation", RCU-GTW-004). Missing ids are generated.
/// </summary>
public sealed class CorrelationMiddleware(RequestDelegate next)
{
    public const string RequestIdHeader = "X-Request-ID";
    public const string CorrelationIdHeader = "X-Correlation-ID";
    private const int MaxLength = 128;

    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var requestId = Clean(context.Request.Headers[RequestIdHeader]) ?? Guid.NewGuid().ToString("N");
        var correlationId = Clean(context.Request.Headers[CorrelationIdHeader]) ?? requestId;

        context.Request.Headers[RequestIdHeader] = requestId;
        context.Request.Headers[CorrelationIdHeader] = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[RequestIdHeader] = requestId;
            context.Response.Headers[CorrelationIdHeader] = correlationId;
            return Task.CompletedTask;
        });

        context.RequestServices.GetService<ScopeContext>()?.SetCorrelation(requestId, correlationId);
        Activity.Current?.SetTag("recuro.request_id", requestId);
        Activity.Current?.SetTag("recuro.correlation_id", correlationId);

        using (LogContext.PushProperty("RequestId", requestId))
        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await next(context);
        }
    }

    // Only accept short, printable ids from clients; anything else is replaced.
    private static string? Clean(string? value) =>
        string.IsNullOrWhiteSpace(value) || value.Length > MaxLength || value.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))
            ? null
            : value;
}

/// <summary>Adds the current correlation ids to outgoing HttpClient calls between services.</summary>
public sealed class CorrelationHeadersHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var headers = accessor.HttpContext?.Request.Headers;
        if (headers is not null)
        {
            foreach (var name in new[] { CorrelationMiddleware.RequestIdHeader, CorrelationMiddleware.CorrelationIdHeader })
            {
                if (!request.Headers.Contains(name) && headers.TryGetValue(name, out var value))
                {
                    request.Headers.TryAddWithoutValidation(name, value.ToString());
                }
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
