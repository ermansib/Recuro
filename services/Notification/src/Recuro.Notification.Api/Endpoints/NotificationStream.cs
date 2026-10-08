using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Web.Http;
using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Application.Feed;
using Recuro.Notification.Application.Feed.Queries;

namespace Recuro.Notification.Api.Endpoints;

/// <summary>Bound from <c>NotificationStream</c>.</summary>
public sealed class NotificationStreamOptions
{
    public const string SectionName = "NotificationStream";

    /// <summary>RCU-GTW-003: at most 30 s between messages, so proxies keep the connection open.</summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Suggested client reconnect delay (SSE <c>retry</c>).</summary>
    public TimeSpan ClientRetry { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// RCU-GTW-003 live bell: <c>GET /stream/notifications</c> as Server-Sent Events, proxied by the gateway.
/// Events: <c>notification.created</c> (data = AppNotification, id = stream position) and
/// <c>heartbeat</c>. A client that reconnects with <c>Last-Event-ID</c> gets everything it missed,
/// because the stream reads from the database rather than from memory.
/// Browsers send the bearer token with a fetch-based SSE client; tokens never go in the URL.
/// </summary>
internal static class NotificationStream
{
    public const string Path = "/stream/notifications";
    public const string CreatedEvent = "notification.created";
    public const string HeartbeatEvent = "heartbeat";
    private const string LastEventIdHeader = "Last-Event-ID";

    public static IEndpointRouteBuilder MapNotificationStream(this IEndpointRouteBuilder app)
    {
        app.MapGet(Path, StreamAsync)
            .RequireAuthorization(NotificationPolicies.Inbox)
            .AddEndpointFilter<RememberContactFilter>()
            .WithTags("Notifications")
            .WithSummary("Live notifications as Server-Sent Events. Resume with Last-Event-ID.")
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream");
        return app;
    }

    private static async Task StreamAsync(
        HttpContext http,
        IQueryHandler<ListNotificationsSinceQuery, FeedPage> feed,
        IFeedChangeListener listener,
        ITenantContext tenant,
        IOptions<NotificationStreamOptions> options,
        CancellationToken ct)
    {
        var first = await feed.Handle(new ListNotificationsSinceQuery(ParseLastEventId(http.Request)), ct);
        if (first.IsFailure)
        {
            await first.Error!.ToProblem().ExecuteAsync(http);
            return;
        }

        http.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();
        http.Response.ContentType = "text/event-stream";
        http.Response.Headers.CacheControl = "no-cache";
        http.Response.Headers["X-Accel-Buffering"] = "no";

        var settings = options.Value;
        var tenantId = tenant.RequiredTenantId;
        await WriteAsync(http, $"retry: {(int)settings.ClientRetry.TotalMilliseconds}\n\n", ct);
        var position = await SendAsync(http, first.Value, ct);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var changed = await listener.WaitAsync(tenantId, settings.HeartbeatInterval, ct);

                // Also re-read on every heartbeat: a missed signal costs latency, never a notification.
                var page = await feed.Handle(new ListNotificationsSinceQuery(position), ct);
                if (page.IsSuccess)
                {
                    position = await SendAsync(http, page.Value, ct);
                }

                if (!changed)
                {
                    await WriteAsync(http, $"event: {HeartbeatEvent}\ndata: {{}}\n\n", ct);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The client went away.
        }
    }

    private static async Task<long> SendAsync(HttpContext http, FeedPage page, CancellationToken ct)
    {
        foreach (var entry in page.Entries)
        {
            var data = JsonSerializer.Serialize(entry.Notification, JsonSerializerOptions.Web);
            await WriteAsync(http, $"id: {entry.Sequence.ToString(CultureInfo.InvariantCulture)}\nevent: {CreatedEvent}\ndata: {data}\n\n", ct);
        }

        return page.Position;
    }

    private static async Task WriteAsync(HttpContext http, string text, CancellationToken ct)
    {
        await http.Response.Body.WriteAsync(Encoding.UTF8.GetBytes(text), ct);
        await http.Response.Body.FlushAsync(ct);
    }

    private static long? ParseLastEventId(HttpRequest request)
    {
        var raw = request.Headers[LastEventIdHeader].FirstOrDefault() ?? request.Query["lastEventId"].FirstOrDefault();
        return long.TryParse(raw, NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : null;
    }
}
