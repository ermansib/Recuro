using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;

namespace Recuro.Candidate.IntegrationTests;

/// <summary>
/// Stands in for the Identity service's masking endpoint with its default map (Identity's
/// <c>DefaultMaskingMap</c>). Set <see cref="Down"/> to simulate an outage.
/// </summary>
public sealed class StubIdentity : HttpMessageHandler
{
    private static readonly Dictionary<string, string> NoAccess = new(StringComparer.Ordinal)
    {
        ["name"] = "hide",
        ["email"] = "hide",
        ["phone"] = "hide",
        ["summary"] = "hide",
        ["currentCtc"] = "hide",
        ["expectedCtc"] = "hide",
    };

    private readonly ConcurrentDictionary<string, Dictionary<string, string>> _maps = new(StringComparer.Ordinal)
    {
        ["hrta"] = [],
        ["hrhead"] = [],
        ["mdceo"] = new(StringComparer.Ordinal) { ["currentCtc"] = "hide", ["expectedCtc"] = "hide", ["phone"] = "partial", ["email"] = "partial" },
        ["employee"] = NoAccess,
        ["candidate"] = NoAccess,
    };

    public bool Down { get; set; }

    public int Calls => _calls;

    private int _calls;

    public void SetMap(string role, Dictionary<string, string> fields) => _maps[role] = fields;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Interlocked.Increment(ref _calls);
        if (Down)
        {
            throw new HttpRequestException("Identity is down (test).");
        }

        // /api/v1/identity/masking/{role}/{resource}
        var segments = request.RequestUri!.AbsolutePath.Trim('/').Split('/');
        if (segments is ["api", "v1", "identity", "masking", var role, "candidate"] && _maps.TryGetValue(role, out var fields))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new { resource = "candidate", role, version = "test", fields }),
            });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}
