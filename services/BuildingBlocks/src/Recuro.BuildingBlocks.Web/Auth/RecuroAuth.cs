namespace Recuro.BuildingBlocks.Web.Auth;

/// <summary>Role keys, the same strings as the frontend's <c>Role</c> type and the Keycloak realm roles.</summary>
public static class RecuroRoles
{
    public const string HrTa = "hrta";
    public const string HrHead = "hrhead";
    public const string MdCeo = "mdceo";
    public const string Employee = "employee";
    public const string Candidate = "candidate";

    /// <summary>Given to service accounts (OAuth2 client credentials, RCU-AUT-005), never to people.</summary>
    public const string Service = "service";

    public static readonly string[] HrStaff = [HrTa, HrHead, MdCeo];
}

/// <summary>Claim names in Recuro access tokens (Keycloak realm <c>recuro</c>).</summary>
public static class RecuroClaims
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string PreferredUsername = "preferred_username";
    public const string Tenant = "tenant_id";
    public const string Roles = "roles";
}

/// <summary>Bound from the <c>Auth</c> configuration section.</summary>
public sealed class RecuroAuthOptions
{
    public const string SectionName = "Auth";

    /// <summary><c>Oidc</c> (default) validates Keycloak tokens; <c>Development</c> trusts X-Dev-* headers (Development/Testing only).</summary>
    public string Mode { get; set; } = "Oidc";

    /// <summary>Realm URL as the token issuer sees it, e.g. <c>http://localhost:8080/realms/recuro</c>.</summary>
    public string? Authority { get; set; }

    /// <summary>
    /// Optional discovery URL when the service reaches Keycloak on a different host than the browser
    /// does (inside docker compose: <c>http://keycloak:8080/realms/recuro/.well-known/openid-configuration</c>).
    /// </summary>
    public string? MetadataAddress { get; set; }

    public string Audience { get; set; } = "recuro-api";

    public bool IsDevelopmentMode => string.Equals(Mode, "Development", StringComparison.OrdinalIgnoreCase);
}
