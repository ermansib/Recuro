using System.Net;
using System.Net.Http.Json;
using Recuro.Onboarding.Application.Abstractions;

namespace Recuro.Onboarding.Infrastructure.Clients;

internal sealed record IdentityUserResponse(string Id, string? Name, string? Department);

/// <summary>
/// Identity's people lookups (architecture.md, "Identity: users"): one person by id, and a department's
/// head by <c>role=hod&amp;department=…</c>. Identity scopes both to the caller's tenant. A department key
/// Identity refuses (400) or a person it doesn't know (404) is "nobody".
/// </summary>
internal sealed class PeopleDirectoryClient(HttpClient http) : IPeopleDirectory
{
    public async Task<Person?> FindAsync(string userId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var user = await GetAsync<IdentityUserResponse>($"api/v1/identity/users/{Uri.EscapeDataString(userId)}", ct);
        return user is null ? null : ToPerson(user);
    }

    public async Task<Person?> FindDepartmentHeadAsync(string department, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(department);
        var heads = await GetAsync<List<IdentityUserResponse>>($"api/v1/identity/users?role=hod&department={Uri.EscapeDataString(department)}", ct);

        // Identity lists by name; with two heads on record the first decides, the same one every time.
        return heads?.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h.Id)) is { } head ? ToPerson(head) : null;
    }

    private static Person ToPerson(IdentityUserResponse user) =>
        new(user.Id, string.IsNullOrWhiteSpace(user.Name) ? user.Id : user.Name, user.Department ?? string.Empty);

    private async Task<T?> GetAsync<T>(string uri, CancellationToken ct)
        where T : class
    {
        HttpResponseMessage response;
        try
        {
            response = await http.GetAsync(new Uri(uri, UriKind.Relative), ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new DependencyUnavailableException("The Identity service is unreachable.", ex);
        }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new DependencyUnavailableException($"Identity answered {(int)response.StatusCode}.");
            }

            try
            {
                return await response.Content.ReadFromJsonAsync<T>(ct);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new DependencyUnavailableException("Identity returned an answer this service can't read.", ex);
            }
        }
    }
}
