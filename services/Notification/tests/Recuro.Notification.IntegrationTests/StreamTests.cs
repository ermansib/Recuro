using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.Notification.IntegrationTests;

[Collection(NotificationCollection.Name)]
public sealed class StreamTests(NotificationApiFactory api)
{
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task New_notifications_arrive_live_and_a_reconnect_resumes_without_loss()
    {
        var tenant = Guid.NewGuid();
        var client = api.ClientFor(tenant, RecuroRoles.HrTa, "u-live");
        using var cts = new CancellationTokenSource(Wait);

        // Connect "from now": nothing old is replayed.
        await api.PublishAsync("bgv.cleared.v1", new { appId = "APP-OLD" }, tenant: tenant);
        using (var stream = await OpenAsync(client, lastEventId: null, cts.Token))
        {
            await api.PublishAsync("bgv.cleared.v1", new { appId = "APP-LIVE" }, tenant: tenant);

            var live = await stream.NextAsync("notification.created", cts.Token);
            Assert.Equal("BGV cleared — APP-LIVE", live.Data.GetProperty("title").GetString());
            Assert.Equal("hrta", live.Data.GetProperty("recipientRole").GetString());

            // Missed while disconnected:
            stream.Dispose();
            await api.PublishAsync("bgv.cleared.v1", new { appId = "APP-MISSED" }, tenant: tenant);

            using var resumed = await OpenAsync(client, live.Id, cts.Token);
            var missed = await resumed.NextAsync("notification.created", cts.Token);
            Assert.Equal("BGV cleared — APP-MISSED", missed.Data.GetProperty("title").GetString());
        }
    }

    [Fact]
    public async Task Quiet_streams_get_heartbeats_and_never_see_other_tenants()
    {
        var tenant = Guid.NewGuid();
        using var cts = new CancellationTokenSource(Wait);
        using var stream = await OpenAsync(api.ClientFor(tenant, RecuroRoles.HrTa), lastEventId: "0", cts.Token);

        await api.PublishAsync("bgv.cleared.v1", new { appId = "APP-ELSEWHERE" }, tenant: Guid.NewGuid());

        var next = await stream.NextAsync(null, cts.Token);
        Assert.Equal("heartbeat", next.Event);
    }

    [Fact]
    public async Task The_stream_needs_a_signed_in_user()
    {
        var response = await api.CreateClient().GetAsync("/stream/notifications");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<SseReader> OpenAsync(HttpClient client, string? lastEventId, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/stream/notifications");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        if (lastEventId is not null)
        {
            request.Headers.Add("Last-Event-ID", lastEventId);
        }

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        return new SseReader(response, await response.Content.ReadAsStreamAsync(ct));
    }

    private sealed record SseEvent(string? Id, string Event, JsonElement Data);

    /// <summary>Minimal Server-Sent Events parser for tests.</summary>
    private sealed class SseReader(HttpResponseMessage response, Stream body) : IDisposable
    {
        private readonly StreamReader _reader = new(body, Encoding.UTF8);

        public async Task<SseEvent> NextAsync(string? eventName, CancellationToken ct)
        {
            while (true)
            {
                var parsed = await ReadEventAsync(ct);
                if (parsed is not null && (eventName is null || parsed.Event == eventName))
                {
                    return parsed;
                }
            }
        }

        public void Dispose()
        {
            _reader.Dispose();
            response.Dispose();
        }

        private async Task<SseEvent?> ReadEventAsync(CancellationToken ct)
        {
            string? id = null, name = null, data = null;
            while (await _reader.ReadLineAsync(ct) is { } line)
            {
                if (line.Length == 0)
                {
                    return name is null ? null : new SseEvent(id, name, JsonDocument.Parse(data ?? "{}").RootElement);
                }

                var colon = line.IndexOf(':', StringComparison.Ordinal);
                var (field, value) = colon < 0 ? (line, string.Empty) : (line[..colon], line[(colon + 1)..].TrimStart());
                switch (field)
                {
                    case "id": id = value; break;
                    case "event": name = value; break;
                    case "data": data = value; break;
                }
            }

            throw new EndOfStreamException();
        }
    }
}
