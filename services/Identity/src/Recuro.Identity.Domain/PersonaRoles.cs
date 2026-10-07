namespace Recuro.Identity.Domain;

/// <summary>
/// Persona role keys: the same strings as the frontend's <c>Role</c> type and the Keycloak realm roles
/// (FRD §3.2). The domain keeps its own copy so it depends on nothing but the shared kernel.
/// </summary>
public static class PersonaRoles
{
    public const string HrTa = "hrta";
    public const string HrHead = "hrhead";
    public const string MdCeo = "mdceo";
    public const string Employee = "employee";
    public const string Candidate = "candidate";

    /// <summary>Tenant administrator in the admin portal. Not a persona, but mirrored like one.</summary>
    public const string TenantAdmin = "tenant-admin";

    /// <summary>Every persona, most senior first. A user's primary role is the first one they hold.</summary>
    public static readonly IReadOnlyList<string> All = [MdCeo, HrHead, HrTa, Employee, Candidate];

    public static bool IsPersona(string role) => All.Contains(role, StringComparer.Ordinal);

    /// <summary>Roles the user mirror keeps. Keycloak's own defaults (offline_access, default-roles-*) are dropped.</summary>
    public static bool IsMirrored(string role) => IsPersona(role) || role == TenantAdmin;

    /// <summary>The role a person is shown as: the most senior persona role they hold.</summary>
    public static string? Primary(IEnumerable<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        var held = roles.ToHashSet(StringComparer.Ordinal);
        return All.FirstOrDefault(held.Contains);
    }
}
