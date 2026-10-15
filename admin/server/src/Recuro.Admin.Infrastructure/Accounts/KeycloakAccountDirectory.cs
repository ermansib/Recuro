using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Roles;
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
    private const string TenantAttribute = "tenant_id";

    /// <summary>User attribute holding the person's workspace role keys, including tenant-defined ones.</summary>
    private const string WorkspaceRolesAttribute = "workspace_roles";

    private const int MaxListedAccounts = 1000;

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

    public async Task<Result<IReadOnlyList<DirectoryAccount>>> ListTenantAccountsAsync(
        Guid tenantId, IReadOnlySet<string> knownAccountIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(knownAccountIds);
        try
        {
            var token = await GetServiceTokenAsync(ct);
            var query = Uri.EscapeDataString($"{TenantAttribute}:{tenantId}");
            using var request = Authorized(HttpMethod.Get, $"users?q={query}&exact=true&max={MaxListedAccounts}&briefRepresentation=true", token);
            using var response = await http.SendAsync(request, ct);
            EnsureSuccess(response, "list users");
            var users = await response.Content.ReadFromJsonAsync<List<KeycloakUserSummary>>(ct) ?? [];

            var accounts = new List<DirectoryAccount>(users.Count);
            foreach (var user in users.Where(u => !string.IsNullOrEmpty(u.Id)))
            {
                var roles = knownAccountIds.Contains(user.Id!) ? [] : await ReadRealmRoleNamesAsync(user.Id!, token, ct);
                var name = $"{user.FirstName} {user.LastName}".Trim();
                accounts.Add(new DirectoryAccount(user.Id!, name.Length > 0 ? name : user.Username ?? user.Id!, user.Email ?? string.Empty, roles));
            }

            return accounts;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or KeycloakException)
        {
            LogListFailed(logger, tenantId, ex);
            return WorkspaceErrors.AccountServiceUnavailable;
        }
    }

    public async Task<Result> SyncWorkspaceRolesAsync(
        string accountId, IReadOnlyList<string> roleKeys, IReadOnlyCollection<string> workspaceRoleKeys, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(roleKeys);
        ArgumentNullException.ThrowIfNull(workspaceRoleKeys);
        try
        {
            var token = await GetServiceTokenAsync(ct);
            await SetWorkspaceRolesAttributeAsync(accountId, roleKeys, token, ct);

            // Realm roles of the same name keep tokens working for services that still read realm roles.
            var held = await ReadRealmRolesAsync(accountId, token, ct);
            var heldNames = held.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
            var toRemove = held.Where(r => workspaceRoleKeys.Contains(r.Name) && !roleKeys.Contains(r.Name)).ToList();
            var toAdd = new List<KeycloakRole>();
            foreach (var key in roleKeys.Where(k => !heldNames.Contains(k)))
            {
                if (await FindRealmRoleAsync(key, token, ct) is { } role)
                {
                    toAdd.Add(role);
                }
            }

            await ChangeRealmRolesAsync(accountId, HttpMethod.Delete, toRemove, token, ct);
            await ChangeRealmRolesAsync(accountId, HttpMethod.Post, toAdd, token, ct);
            return Result.Success();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or KeycloakException)
        {
            LogSyncFailed(logger, accountId, ex);
            return WorkspaceErrors.AccountServiceUnavailable;
        }
    }

    public async Task<Result<string>> CreateWorkspacePersonAsync(NewWorkspacePerson person, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(person);
        try
        {
            var token = await GetServiceTokenAsync(ct);
            var accountId = await CreateUserAsync(
                person.TenantId, person.Name, person.Email, person.RoleKeys, person.TemporaryPassword, temporary: true, token, ct);
            if (accountId.IsFailure)
            {
                return accountId;
            }

            try
            {
                // Realm roles of the same name keep tokens working for services that still read realm roles.
                var realmRoles = new List<KeycloakRole>();
                foreach (var key in person.RoleKeys.Distinct(StringComparer.Ordinal))
                {
                    if (await FindRealmRoleAsync(key, token, ct) is { } role)
                    {
                        realmRoles.Add(role);
                    }
                }

                await ChangeRealmRolesAsync(accountId.Value, HttpMethod.Post, realmRoles, token, ct);
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
            LogCreatePersonFailed(logger, person.TenantId, ex);
            return WorkspaceErrors.AccountServiceUnavailable;
        }
    }

    public async Task<Result> SetTemporaryPasswordAsync(Guid tenantId, string accountId, string password, CancellationToken ct)
    {
        try
        {
            var token = await GetServiceTokenAsync(ct);
            var path = $"users/{Uri.EscapeDataString(accountId)}";

            // The admin database already scopes people to the workspace; the account's own tenant_id must agree.
            using var read = Authorized(HttpMethod.Get, path, token);
            using var found = await http.SendAsync(read, ct);
            if (found.StatusCode == HttpStatusCode.NotFound)
            {
                return WorkspaceMember.NotFound(accountId);
            }

            EnsureSuccess(found, "read user");
            var user = await found.Content.ReadFromJsonAsync<JsonObject>(ct);
            var tenants = (user?["attributes"] as JsonObject)?[TenantAttribute] as JsonArray;
            if (tenants is null || !tenants.Any(t => string.Equals(t?.GetValue<string>(), tenantId.ToString(), StringComparison.OrdinalIgnoreCase)))
            {
                return WorkspaceMember.NotFound(accountId);
            }

            using var reset = Authorized(HttpMethod.Put, $"{path}/reset-password", token);
            reset.Content = JsonContent.Create(new KeycloakCredential("password", password, Temporary: true));
            using var response = await http.SendAsync(reset, ct);
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                // Keycloak refuses passwords that break the realm's own policy (including password history).
                return WorkspaceErrors.WeakPassword;
            }

            EnsureSuccess(response, "reset the password");
            return Result.Success();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or KeycloakException)
        {
            LogResetFailed(logger, accountId, ex);
            return WorkspaceErrors.AccountServiceUnavailable;
        }
    }

    /// <summary>Read-modify-write of the whole user, because Keycloak replaces the attribute map on update.</summary>
    private async Task SetWorkspaceRolesAttributeAsync(string accountId, IReadOnlyList<string> roleKeys, string token, CancellationToken ct)
    {
        var path = $"users/{Uri.EscapeDataString(accountId)}";
        using var read = Authorized(HttpMethod.Get, path, token);
        using var found = await http.SendAsync(read, ct);
        EnsureSuccess(found, "read user");
        var user = await found.Content.ReadFromJsonAsync<JsonObject>(ct) ?? throw new KeycloakException("Keycloak returned no user.");

        var attributes = user["attributes"] as JsonObject ?? [];
        attributes[WorkspaceRolesAttribute] = new JsonArray([.. roleKeys.Select(k => JsonValue.Create(k))]);
        user["attributes"] = attributes;

        using var write = Authorized(HttpMethod.Put, path, token);
        write.Content = JsonContent.Create(user);
        using var response = await http.SendAsync(write, ct);
        EnsureSuccess(response, "update user");
    }

    private async Task<List<KeycloakRole>> ReadRealmRolesAsync(string accountId, string token, CancellationToken ct)
    {
        using var request = Authorized(HttpMethod.Get, $"users/{Uri.EscapeDataString(accountId)}/role-mappings/realm", token);
        using var response = await http.SendAsync(request, ct);
        EnsureSuccess(response, "read role mappings");
        return await response.Content.ReadFromJsonAsync<List<KeycloakRole>>(ct) ?? [];
    }

    private async Task<IReadOnlyList<string>> ReadRealmRoleNamesAsync(string accountId, string token, CancellationToken ct) =>
        (await ReadRealmRolesAsync(accountId, token, ct)).Select(r => r.Name).ToList();

    private async Task<KeycloakRole?> FindRealmRoleAsync(string name, string token, CancellationToken ct)
    {
        using var lookup = Authorized(HttpMethod.Get, $"roles/{Uri.EscapeDataString(name)}", token);
        using var found = await http.SendAsync(lookup, ct);
        if (found.StatusCode == HttpStatusCode.NotFound)
        {
            // A tenant-defined role: it lives only in the workspace_roles attribute.
            return null;
        }

        EnsureSuccess(found, $"read role {name}");
        return await found.Content.ReadFromJsonAsync<KeycloakRole>(ct);
    }

    private async Task ChangeRealmRolesAsync(string accountId, HttpMethod method, List<KeycloakRole> roles, string token, CancellationToken ct)
    {
        if (roles.Count == 0)
        {
            return;
        }

        using var request = Authorized(method, $"users/{Uri.EscapeDataString(accountId)}/role-mappings/realm", token);
        request.Content = JsonContent.Create(roles);
        using var response = await http.SendAsync(request, ct);
        EnsureSuccess(response, "change role mappings");
    }

    private Task<Result<string>> CreateUserAsync(NewWorkspaceOwner owner, string token, CancellationToken ct) =>
        CreateUserAsync(owner.TenantId, owner.Name, owner.Email, [owner.Role], owner.Password, owner.MustChangePassword, token, ct);

    private async Task<Result<string>> CreateUserAsync(
        Guid tenantId, string name, string email, IReadOnlyList<string> roleKeys, string password, bool temporary, string token, CancellationToken ct)
    {
        var (firstName, lastName) = SplitName(name);
        var body = new KeycloakUser(
            email,
            email,
            firstName,
            lastName,
            Enabled: true,
            EmailVerified: false,
            new Dictionary<string, string[]>
            {
                [TenantAttribute] = [tenantId.ToString()],
                [WorkspaceRolesAttribute] = [.. roleKeys],
            },
            [new KeycloakCredential("password", password, Temporary: temporary)]);

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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Listing Keycloak accounts failed for tenant {TenantId}")]
    private static partial void LogListFailed(ILogger logger, Guid tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Syncing workspace roles to Keycloak account {AccountId} failed")]
    private static partial void LogSyncFailed(ILogger logger, string accountId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Creating a person's account in Keycloak failed for tenant {TenantId}")]
    private static partial void LogCreatePersonFailed(ILogger logger, Guid tenantId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Setting a temporary password for Keycloak account {AccountId} failed")]
    private static partial void LogResetFailed(ILogger logger, string accountId, Exception exception);

    private sealed record KeycloakUserSummary(string? Id, string? Username, string? Email, string? FirstName, string? LastName);

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
