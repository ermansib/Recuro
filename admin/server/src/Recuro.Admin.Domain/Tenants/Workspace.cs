using Recuro.Admin.Domain.Common;

namespace Recuro.Admin.Domain.Tenants;

/// <summary>Kind of organisation, as chosen on the sign-up page. Same keys as the frontend's <c>OrgType</c>.</summary>
public enum OrgType
{
    SmallBusiness,
    Agency,
    Enterprise,
}

/// <summary>Settings a new workspace starts with when nothing else is known.</summary>
public static class WorkspaceDefaults
{
    public const string Locale = "en-IN";
    public const string Currency = "INR";
    public const int SessionIdleMinutes = 30;
}

/// <summary>
/// Starting settings per org type, mirroring <c>ORG_TYPE_DEFAULTS</c> in frontend/src/domain/auth.ts. Org type
/// only changes these defaults: every organisation gets the same features and can change any of this later.
/// </summary>
public sealed record OrgTypeDefaults(string OwnerRole, IReadOnlyList<string> SsoProviders, IReadOnlyList<string> MfaRoles, string CareersTagline)
{
    private static readonly string[] ElevatedRoles = [WorkspaceRoles.HrHead, WorkspaceRoles.MdCeo];

    public static OrgTypeDefaults For(OrgType orgType) => orgType switch
    {
        OrgType.Agency => new(WorkspaceRoles.HrHead, ["google", "microsoft"], ElevatedRoles, "Find your next role through us"),
        OrgType.Enterprise => new(WorkspaceRoles.HrHead, ["microsoft", "google", "saml"], ElevatedRoles, "Grow your career where it matters"),
        _ => new(WorkspaceRoles.HrTa, ["google", "microsoft"], ElevatedRoles, "Build something great with us"),
    };
}

/// <summary>Persona role keys a workspace owner can take: the Keycloak realm roles and the frontend's staff roles.</summary>
public static class WorkspaceRoles
{
    public const string HrTa = "hrta";
    public const string HrHead = "hrhead";
    public const string MdCeo = "mdceo";

    public static readonly IReadOnlyList<string> OwnerRoles = [HrTa, HrHead, MdCeo];
}

/// <summary>RCU-PLT-001 password policy, the same rules as <c>unmetPasswordRules</c> in the frontend.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 8;

    public static bool IsSatisfiedBy(string? password) =>
        password is { Length: >= MinLength }
        && password.Any(char.IsAsciiLetterUpper)
        && password.Any(char.IsAsciiLetterLower)
        && password.Any(char.IsAsciiDigit)
        && password.Any(c => !char.IsAsciiLetterOrDigit(c));
}

public static class WorkspaceErrors
{
    public static readonly Error OrganisationRequired =
        Error.Validation("workspace.orgName", "Organisation and your name are required.");

    public static readonly Error InvalidEmail = Error.Validation("workspace.email", "Enter a valid email address.");

    public static readonly Error WeakPassword = Error.Validation("workspace.password", "Password does not meet the policy.");

    public static readonly Error TermsNotAccepted = Error.Validation("workspace.terms", "Accept the terms to create a workspace.");

    public static readonly Error InvalidOwnerRole =
        Error.Validation("workspace.adminRole", "Choose HR-TA, HR Head or MD/CEO as your role.");

    public static readonly Error EmailTaken =
        Error.Conflict("workspace.emailTaken", "An account with this email already exists. Sign in instead, or use another email.");

    public static readonly Error AccountServiceUnavailable =
        Error.Unavailable("workspace.accountsUnavailable", "We couldn't create your account right now. Please try again in a few minutes.");

    public static Error NotFound(string slug) =>
        Error.NotFound("workspace.notFound", $"We couldn't find the workspace '{slug}'.");
}
