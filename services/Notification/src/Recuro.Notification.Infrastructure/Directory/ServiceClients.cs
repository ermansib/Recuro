using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Recuro.Notification.Application.Abstractions;

namespace Recuro.Notification.Infrastructure.Directory;

/// <summary>Bound from <c>Services:Candidate</c> and <c>Services:Identity</c>.</summary>
public sealed class ServiceEndpointOptions
{
    public const string CandidateSection = "Services:Candidate";
    public const string IdentitySection = "Services:Identity";

    /// <summary>The service's base URL, e.g. <c>http://localhost:5107/</c>.</summary>
    public Uri? BaseUrl { get; set; }

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Candidate's public contract (<c>GET /api/v1/candidates/{id}</c>), called as this service: the service
/// role sees the name and email unmasked (Identity masking map masking-2026.10.1).
/// </summary>
internal sealed class CandidateContactsClient(HttpClient http) : ICandidateContacts
{
    public async Task<CandidateContact?> FindAsync(string candidateId, CancellationToken ct)
    {
        if (!Guid.TryParse(candidateId, out var id))
        {
            return null;
        }

        using var response = await http.GetAsync(new Uri($"api/v1/candidates/{id}", UriKind.Relative), ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        // Anything else unexpected throws, so the event is retried rather than the email suppressed.
        response.EnsureSuccessStatusCode();
        var candidate = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        var email = Text(candidate, "email");
        return IsAddress(email) ? new CandidateContact(Text(candidate, "name"), email!) : null;
    }

    /// <summary>A masked or anonymised value is not an address.</summary>
    private static bool IsAddress(string? email) =>
        !string.IsNullOrWhiteSpace(email) && email.Contains('@', StringComparison.Ordinal) && !email.Contains('*', StringComparison.Ordinal);

    private static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}

/// <summary>Identity's <c>GET /api/v1/identity/users?role=</c>, called as this service.</summary>
internal sealed partial class IdentityStaffDirectory(HttpClient http, ILogger<IdentityStaffDirectory> logger) : IStaffDirectory
{
    public async Task<IReadOnlyList<StaffContact>?> UsersInRoleAsync(string role, CancellationToken ct)
    {
        try
        {
            var users = await http.GetFromJsonAsync<List<IdentityUser>>(
                new Uri($"api/v1/identity/users?role={Uri.EscapeDataString(role)}", UriKind.Relative), ct);
            return users?.Select(u => new StaffContact(u.Id ?? string.Empty, u.Name, u.Email)).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException
                                   || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            IdentityUnavailable(logger, role, ex.GetType().Name);
            return null;
        }
    }

    private sealed record IdentityUser(string? Id, string? Name, string? Email);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Identity could not list role {Role} ({Reason}); using the local directory")]
    private static partial void IdentityUnavailable(ILogger logger, string role, string reason);
}
