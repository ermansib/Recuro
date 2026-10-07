using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Recuro.BuildingBlocks.Application.Abstractions;

namespace Recuro.BuildingBlocks.Web.Middleware;

/// <summary>
/// RCU-PLT-004: a mutating request with an <c>Idempotency-Key</c> runs once; replays within 24 hours
/// get the original status and body back, marked with <c>Idempotent-Replayed: true</c>. Keys are
/// scoped to tenant, user, method and path, so one caller can't replay another's response.
/// Server errors (5xx) are not cached, so the client can retry them.
/// </summary>
public sealed class IdempotencyMiddleware(RequestDelegate next)
{
    public const string KeyHeader = "Idempotency-Key";
    public const string ReplayedHeader = "Idempotent-Replayed";
    private const int MaxKeyLength = 100;
    private static readonly TimeSpan Retention = TimeSpan.FromHours(24);

    public async Task InvokeAsync(HttpContext context, IDistributedCache cache, ScopeContext scope)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(scope);
        var key = context.Request.Headers[KeyHeader].ToString();
        if (string.IsNullOrEmpty(key) || !IsMutating(context.Request.Method))
        {
            await next(context);
            return;
        }

        if (key.Length > MaxKeyLength)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { title = $"{KeyHeader} must be at most {MaxKeyLength} characters." });
            return;
        }

        var cacheKey = $"idem:{scope.TenantId}:{scope.UserId}:{context.Request.Method}:{context.Request.Path}:{key}";
        var cached = await cache.GetAsync(cacheKey, context.RequestAborted);
        if (cached is not null)
        {
            await ReplayAsync(context, JsonSerializer.Deserialize<StoredResponse>(cached)!);
            return;
        }

        var original = context.Response.Body;
        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;
        try
        {
            await next(context);
        }
        finally
        {
            context.Response.Body = original;
        }

        var body = buffer.ToArray();
        if (context.Response.StatusCode < StatusCodes.Status500InternalServerError)
        {
            var stored = new StoredResponse(context.Response.StatusCode, context.Response.ContentType, body, context.Response.Headers.Location);
            await cache.SetAsync(
                cacheKey,
                JsonSerializer.SerializeToUtf8Bytes(stored),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = Retention },
                context.RequestAborted);
        }

        await original.WriteAsync(body, context.RequestAborted);
    }

    private static bool IsMutating(string method) =>
        HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method) || HttpMethods.IsDelete(method);

    private static async Task ReplayAsync(HttpContext context, StoredResponse stored)
    {
        context.Response.StatusCode = stored.Status;
        context.Response.ContentType = stored.ContentType;
        if (!string.IsNullOrEmpty(stored.Location))
        {
            context.Response.Headers.Location = stored.Location;
        }

        context.Response.Headers[ReplayedHeader] = "true";
        await context.Response.Body.WriteAsync(stored.Body, context.RequestAborted);
    }

    private sealed record StoredResponse(int Status, string? ContentType, byte[] Body, string? Location);
}
