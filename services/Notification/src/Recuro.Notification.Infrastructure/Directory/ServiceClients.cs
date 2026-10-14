using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Recuro.Notification.Application.Abstractions;

namespace Recuro.Notification.Infrastructure.Directory;

/// <summary>Bound from <c>Services:Candidate</c>, <c>Services:Identity</c> and <c>Services:Requisition</c>.</summary>
public sealed class ServiceEndpointOptions
{
    public const string CandidateSection = "Services:Candidate";
    public const string IdentitySection = "Services:Identity";
    public const string RequisitionSection = "Services:Requisition";

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
    public Task<IReadOnlyList<StaffContact>?> UsersInRoleAsync(string role, CancellationToken ct) =>
        ListAsync($"api/v1/identity/users?role={Uri.EscapeDataString(role)}", role, ct);

    public Task<IReadOnlyList<StaffContact>?> DepartmentHeadsAsync(string department, CancellationToken ct) =>
        ListAsync($"api/v1/identity/users?role=hod&department={Uri.EscapeDataString(department)}", $"hod/{department}", ct);

    private async Task<IReadOnlyList<StaffContact>?> ListAsync(string path, string role, CancellationToken ct)
    {
        try
        {
            var users = await http.GetFromJsonAsync<List<IdentityUser>>(new Uri(path, UriKind.Relative), ct);
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

/// <summary>Requisition's <c>GET /api/v1/requisitions/{reqId}</c>, called as this service (requisitions.read allows it).</summary>
internal sealed partial class RequisitionLookupClient(HttpClient http, ILogger<RequisitionLookupClient> logger) : IRequisitionLookup
{
    public async Task<string?> DepartmentOfAsync(string reqId, CancellationToken ct)
    {
        try
        {
            using var response = await http.GetAsync(new Uri($"api/v1/requisitions/{Uri.EscapeDataString(reqId)}", UriKind.Relative), ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            var requisition = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
            return requisition.ValueKind == JsonValueKind.Object && requisition.TryGetProperty("department", out var department)
                   && department.ValueKind == JsonValueKind.String
                ? department.GetString()
                : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or NotSupportedException
                                   || (ex is OperationCanceledException && !ct.IsCancellationRequested))
        {
            // The HOD is an extra recipient: without the department the rest of the notice still goes out.
            RequisitionUnavailable(logger, reqId, ex.GetType().Name);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Requisition could not say the department of {ReqId} ({Reason}); no department head notified")]
    private static partial void RequisitionUnavailable(ILogger logger, string reqId, string reason);
}
