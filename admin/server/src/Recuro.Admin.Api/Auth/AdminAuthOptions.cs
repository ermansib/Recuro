namespace Recuro.Admin.Api.Auth;

/// <summary>The "Auth" configuration section.</summary>
public sealed class AdminAuthOptions
{
    public const string SectionName = "Auth";
    public const string OidcMode = "Oidc";
    public const string DevelopmentMode = "Development";

    /// <summary>"Oidc" (Keycloak or any OpenID Connect provider) or "Development" (persona header, local only).</summary>
    public string Mode { get; set; } = OidcMode;

    /// <summary>Issuer URL, for example http://localhost:8080/realms/recuro.</summary>
    public string? Authority { get; set; }

    /// <summary>Audience the API requires in access tokens.</summary>
    public string Audience { get; set; } = "recuro-admin-api";

    /// <summary>Public client the browser signs in with (authorization code + PKCE).</summary>
    public string ClientId { get; set; } = "recuro-admin";

    /// <summary>Claim carrying the user's roles. Keycloak's realm roles are mapped to "roles" by the realm import.</summary>
    public string RoleClaim { get; set; } = "roles";

    public bool IsDevelopmentMode => string.Equals(Mode, DevelopmentMode, StringComparison.OrdinalIgnoreCase);
}
