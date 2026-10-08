using System.Globalization;
using System.Net.Http.Json;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.Interview.Application.Abstractions;

namespace Recuro.Interview.Infrastructure.Clients;

internal sealed record DecideResponse(bool Allow, IReadOnlyList<string>? Reasons);

/// <summary>
/// Identity's PDP (<c>POST /api/v1/identity/decide</c>, RCU-AUT-003). Default deny: no answer from
/// Identity is a 503, never an allow.
/// </summary>
public sealed class IdentityAccessDecisions(HttpClient http, ICurrentUser user) : IAccessDecisions
{
    public async Task<bool> AllowedAsync(string action, string resourceType, string resourceId, IReadOnlyList<string> assigneeIds, CancellationToken ct)
    {
        var body = new
        {
            actor = new { id = user.UserId, roles = user.Roles },
            action,
            resource = new { type = resourceType, id = resourceId, assigneeIds },
            context = new { mfa = false },
        };

        HttpResponseMessage response;
        try
        {
            response = await http.PostAsJsonAsync(new Uri("api/v1/identity/decide", UriKind.Relative), body, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new DependencyUnavailableException("The Identity service is unreachable.", ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
            {
                throw new DependencyUnavailableException(
                    string.Create(CultureInfo.InvariantCulture, $"Identity answered {(int)response.StatusCode} to a policy decision."));
            }

            var decision = await response.Content.ReadFromJsonAsync<DecideResponse>(ct);
            return decision?.Allow == true;
        }
    }
}
