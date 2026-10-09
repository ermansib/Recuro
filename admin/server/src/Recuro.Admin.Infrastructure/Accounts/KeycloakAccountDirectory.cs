using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Tenants;

namespace Recuro.Admin.Infrastructure.Accounts;

/// <summary>
/// Creates workspace owners in Keycloak through its admin REST API, signed in as the
/// <see cref="AccountDirectoryOptions.ClientId"/> service account (client credentials).
/// Keycloak keeps the password; the admin database never sees it.
/// </summary>
internal sealed partial class KeycloakAccountDirectory(
    HttpClient http,
    IOptions<AccountDirectoryOptions> options,
    ILogger<KeycloakAccountDirectory> logger) : IAccountDirectory
{
    private readonly AccountDirectoryOptions _options = options.Value;

    public async Task<Result<string>> CreateWorkspaceOwnerAsync(NewWorkspaceOwner owner, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(owner);
        try
        {
            var token = await GetServiceTokenAsync(ct);
            var accountId = await CreateUserAsync(owner, token, ct);
            if (accountId.IsFailure)
            {
                return accountId;
            }

            try
            {
                await AssignRealmRolesAsync(accountId.Value, [owner.Role, _options.OwnerAdminRole], token, ct);
            }
            catch
            {
                await DeleteAccountAsync(accountId.Value, CancellationToken.None);
                throw;
            }

            return accountId;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or KeycloakException)
        {
            LogKeycloakFailed(logger, owner.TenantId, ex);
            return WorkspaceErrors.AccountServiceUnavailable;
        }
    }

    public async Task DeleteAccountAsync(string accountId, CancellationToken ct)
    {
        try
        {
            var token = await GetServiceTokenAsync(ct);
            using var request = Authorized(HttpMethod.Delete, $"users/{Uri.EscapeDataString(accountId)}", token);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.NotFound)
            {
                LogCleanupFailed(logger, accountId, (int)response.StatusCode);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or KeycloakException)
        {
            LogCleanupFailed(logger, accountId, 0);
        }
    }

    private async Task<Result<string>> CreateUserAsync(NewWorkspaceOwner owner, string token, CancellationToken ct)
    {
        var (firstName, lastName) = SplitName(owner.Name);
        var body = new KeycloakUser(
            owner.Email,
            owner.Email,
            firstName,
            lastName,
            Enabled: true,
            EmailVerified: false,
            new Dictionary<string, string[]> { ["tenant_id"] = [owner.TenantId.ToString()] },
            [new KeycloakCredential("password", owner.Password, Temporary: false)]);

        using var request = Authorized(HttpMethod.Post, "users", token);
        request.Content = JsonContent.Create(body);
        using var response = await http.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.Conflict)
        {
            return WorkspaceErrors.EmailTaken;
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            // Keycloak refuses passwords that break the realm's own policy.
            return WorkspaceErrors.WeakPassword;
        }

        EnsureSuccess(response, "create user");
        var location = response.Headers.Location?.ToString();
        var accountId = location?[(location.LastIndexOf('/') + 1)..];
        return string.IsNullOrEmpty(accountId)
            ? throw new KeycloakException("Keycloak did not return the new user's id.")
            : accountId;
    }

    private async Task AssignRealmRolesAsync(string accountId, IEnumerable<string> roleNames, string token, CancellationToken ct)
    {
        var roles = new List<KeycloakRole>();
        foreach (var name in roleNames.Distinct(StringComparer.Ordinal))
        {
            using var lookup = Authorized(HttpMethod.Get, $"roles/{Uri.EscapeDataString(name)}", token);
            using var found = await http.SendAsync(lookup, ct);
            EnsureSuccess(found, $"read role {name}");
            roles.Add(await found.Content.ReadFromJsonAsync<KeycloakRole>(ct)
                ?? throw new KeycloakException($"Keycloak returned no role '{name}'."));
        }

        using var request = Authorized(HttpMethod.Post, $"users/{Uri.EscapeDataString(accountId)}/role-mappings/realm", token);
        request.Content = JsonContent.Create(roles);
        using var response = await http.SendAsync(request, ct);
        EnsureSuccess(response, "assign roles");
    }

    private async Task<string> GetServiceTokenAsync(CancellationToken ct)
    {
        var url = new Uri($"{_options.BaseUrl.TrimEnd('/')}/realms/{Uri.EscapeDataString(_options.Realm)}/protocol/openid-connect/token");
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _options.ClientId,
            ["client_secret"] = _options.ClientSecret,
        });
        using var response = await http.PostAsync(url, form, ct);
        EnsureSuccess(response, "get a service token");
        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        return token?.AccessToken ?? throw new KeycloakException("Keycloak returned no access token.");
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        var url = new Uri($"{_options.BaseUrl.TrimEnd('/')}/admin/realms/{Uri.EscapeDataString(_options.Realm)}/{path}");
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static void EnsureSuccess(HttpResponseMessage response, string step)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new KeycloakException($"Keycloak could not {step}: HTTP {(int)response.StatusCode}.");
        }
    }

    private static (string First, string Last) SplitName(string name)
    {
        var parts = name.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (parts.ElementAtOrDefault(0) ?? name, parts.ElementAtOrDefault(1) ?? string.Empty);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Creating the owner account in Keycloak failed for tenant {TenantId}")]
    private static partial void LogKeycloakFailed(ILogger logger, Guid tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not remove Keycloak account {AccountId} after a failed sign-up (HTTP {Status})")]
    private static partial void LogCleanupFailed(ILogger logger, string accountId, int status);

    private sealed class KeycloakException(string message) : Exception(message);

    private sealed record KeycloakUser(
        string Username,
        string Email,
        string FirstName,
        string LastName,
        bool Enabled,
        bool EmailVerified,
        Dictionary<string, string[]> Attributes,
        KeycloakCredential[] Credentials);

    private sealed record KeycloakCredential(string Type, string Value, bool Temporary);

    private sealed record KeycloakRole(string Id, string Name);

    private sealed record TokenResponse([property: JsonPropertyName("access_token")] string? AccessToken);
}
