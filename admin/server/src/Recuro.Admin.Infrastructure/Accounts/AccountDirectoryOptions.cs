namespace Recuro.Admin.Infrastructure.Accounts;

/// <summary>The "AccountDirectory" configuration section: where workspace owners' accounts are created.</summary>
public sealed class AccountDirectoryOptions
{
    public const string SectionName = "AccountDirectory";
    public const string KeycloakMode = "Keycloak";
    public const string DisabledMode = "Disabled";

    /// <summary>
    /// "Keycloak" (default) creates accounts through Keycloak's admin REST API. "Disabled" creates none and is
    /// only for runs without Keycloak (tests, the development persona sign-in).
    /// </summary>
    public string Mode { get; set; } = KeycloakMode;

    /// <summary>Keycloak's base URL, for example http://127.0.0.1:8080.</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string Realm { get; set; } = "recuro";

    /// <summary>Confidential client whose service account may manage users (realm-management manage-users, view-realm).</summary>
    public string ClientId { get; set; } = "recuro-admin-provisioner";

    /// <summary>Set through user secrets or an environment variable, never in the repository outside local dev.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Realm role every workspace owner gets besides their persona role, so they can open the tenant admin.</summary>
    public string OwnerAdminRole { get; set; } = "tenant-admin";

    public bool IsDisabled => string.Equals(Mode, DisabledMode, StringComparison.OrdinalIgnoreCase);
}
