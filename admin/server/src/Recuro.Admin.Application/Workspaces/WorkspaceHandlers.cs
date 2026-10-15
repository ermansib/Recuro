using System.Net.Mail;
using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Roles;
using Recuro.Admin.Domain.Tenants;

namespace Recuro.Admin.Application.Workspaces;

/// <summary>
/// The shared create-workspace fields (admin/contracts/workspace-form): the same on the portal's sign-up page
/// and in the platform console, so a workspace always starts with an owner who can sign in.
/// </summary>
public sealed record NewWorkspace(string OrgName, OrgType OrgType, string AdminName, string AdminRole, string Email, string Password);

/// <summary>What only the platform console chooses. Sign-up uses <see cref="SelfService"/>.</summary>
/// <param name="OwnerMustChangePassword">True when someone else chose the password, so the owner replaces it at first sign-in.</param>
public sealed record WorkspaceSettings(TenantPlan Plan, string? CustomDomain, bool OwnerMustChangePassword)
{
    public static readonly WorkspaceSettings SelfService = new(TenantPlan.Starter, null, OwnerMustChangePassword: false);
}

public sealed record OpenedWorkspace(Tenant Tenant, WorkspaceOwnerDto Owner);

/// <summary>
/// Opens a workspace from the shared create-workspace form: a row in <c>tenants</c>, the owner's account in the
/// account directory, and the workspace's default roles with the owner as its first member. The account is
/// created first; if saving the workspace then fails, it is removed again, so a failure never leaves an
/// account without a workspace. Used by the sign-up page and the platform console alike.
/// </summary>
public sealed class OpenWorkspaceHandler(
    ITenantRepository tenants,
    IWorkspaceRoleRepository roles,
    IWorkspaceMemberRepository members,
    IUnitOfWork unitOfWork,
    IAccountDirectory accounts,
    SignUpDefaults defaults,
    IClock clock)
{
    private const int MaxSlugSuffix = 1000;

    public async Task<Result<OpenedWorkspace>> HandleAsync(NewWorkspace details, WorkspaceSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(settings);

        var invalid = Validate(details);
        if (invalid is not null)
        {
            return invalid;
        }

        var email = details.Email.Trim().ToLowerInvariant();
        var slug = await UniqueSlugAsync(details.OrgName, ct);
        var created = Tenant.Open(
            details.OrgName, slug, details.OrgType, settings.Plan, email.Split('@')[1], defaults.Locale, defaults.Currency, clock.UtcNow);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        var tenant = created.Value;
        var domain = tenant.SetCustomDomain(settings.CustomDomain);
        if (domain.IsFailure)
        {
            return domain.Error!;
        }

        if (tenant.CustomDomain is not null && await tenants.CustomDomainExistsAsync(tenant.CustomDomain, ct))
        {
            return TenantErrors.DomainTaken;
        }

        var ownerName = details.AdminName.Trim();
        var account = await accounts.CreateWorkspaceOwnerAsync(
            new NewWorkspaceOwner(tenant.Id, ownerName, email, details.AdminRole, details.Password, settings.OwnerMustChangePassword), ct);
        if (account.IsFailure)
        {
            return account.Error!;
        }

        try
        {
            tenants.Add(tenant);
            roles.AddRange(DefaultWorkspaceRoles.CreateFor(tenant.Id));
            members.Add(WorkspaceMember.Create(tenant.Id, account.Value, ownerName, email, [details.AdminRole]));
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            await accounts.DeleteAccountAsync(account.Value, CancellationToken.None);
            throw;
        }

        return new OpenedWorkspace(tenant, new WorkspaceOwnerDto(account.Value, ownerName, email, details.AdminRole));
    }

    /// <summary>The same rules, in the same order, as <c>validateWorkspaceForm</c> in admin/contracts/workspace-form.</summary>
    private static Error? Validate(NewWorkspace details)
    {
        if (!Enum.IsDefined(details.OrgType))
        {
            return WorkspaceErrors.InvalidOrgType;
        }

        var orgName = details.OrgName?.Trim() ?? string.Empty;
        if (orgName.Length is 0 or > WorkspaceForm.OrgNameMaxLength)
        {
            return WorkspaceErrors.OrganisationRequired;
        }

        var ownerName = details.AdminName?.Trim() ?? string.Empty;
        if (ownerName.Length is 0 or > WorkspaceForm.OwnerNameMaxLength)
        {
            return WorkspaceErrors.OwnerNameRequired;
        }

        if (!WorkspaceRoles.OwnerRoles.Contains(details.AdminRole))
        {
            return WorkspaceErrors.InvalidOwnerRole;
        }

        if (!IsValidEmail(details.Email))
        {
            return WorkspaceErrors.InvalidEmail;
        }

        return PasswordPolicy.IsSatisfiedBy(details.Password) ? null : WorkspaceErrors.WeakPassword;
    }

    private static bool IsValidEmail(string? email) =>
        !string.IsNullOrWhiteSpace(email)
        && MailAddress.TryCreate(email.Trim(), out var address)
        && address.Address == email.Trim()
        && address.Host.Contains('.', StringComparison.Ordinal);

    /// <summary>`Acme Corp` → `acme-corp`, then `acme-corp-2` and so on if that is taken.</summary>
    private async Task<string> UniqueSlugAsync(string orgName, CancellationToken ct)
    {
        var baseSlug = WorkspaceSlug.From(orgName);
        var slug = baseSlug;
        for (var n = 2; n < MaxSlugSuffix && await tenants.SlugExistsAsync(slug, ct); n++)
        {
            var suffix = $"-{n}";
            slug = baseSlug[..Math.Min(baseSlug.Length, Tenant.SlugMaxLength - suffix.Length)].TrimEnd('-') + suffix;
        }

        return slug;
    }
}

/// <summary>Body of the public sign-up form (RCU-PLT-001), the frontend's <c>RegisterOrganisationInput</c>.</summary>
public sealed record SignUpWorkspaceRequest(
    string OrgName,
    OrgType OrgType,
    string AdminName,
    string AdminRole,
    string Email,
    string Password,
    bool AcceptTerms);

/// <summary>
/// Creates a workspace from the public sign-up page: the shared create-workspace form plus the terms, on the
/// Starter plan, with the password the owner chose.
/// </summary>
public sealed class SignUpWorkspaceHandler(OpenWorkspaceHandler open)
{
    public async Task<Result<WorkspaceSignUpDto>> HandleAsync(SignUpWorkspaceRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.AcceptTerms)
        {
            return WorkspaceErrors.TermsNotAccepted;
        }

        var details = new NewWorkspace(request.OrgName, request.OrgType, request.AdminName, request.AdminRole, request.Email, request.Password);
        var opened = await open.HandleAsync(details, WorkspaceSettings.SelfService, ct);
        return opened.IsFailure
            ? opened.Error!
            : new WorkspaceSignUpDto(WorkspaceDto.From(opened.Value.Tenant), opened.Value.Owner);
    }
}

/// <summary>
/// Public workspace lookup, for the sign-in page of a workspace that isn't known to this browser yet. People type
/// the workspace's address (<c>mb-consulting</c>) or its name (<c>MB Consulting</c>); both find it. A name two
/// active workspaces share finds neither, so a lookup never guesses between workspaces.
/// </summary>
public sealed class GetWorkspaceHandler(ITenantRepository tenants)
{
    public async Task<Result<WorkspaceDto>> HandleAsync(string slugOrName, CancellationToken ct)
    {
        var wanted = slugOrName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(wanted))
        {
            return WorkspaceErrors.NotFound(wanted);
        }

        var tenant = await ActiveBySlugAsync(wanted, ct)
            ?? await ActiveBySlugAsync(WorkspaceSlug.From(wanted), ct)
            ?? await UniqueActiveByNameAsync(wanted, ct);
        return tenant is null ? WorkspaceErrors.NotFound(wanted) : WorkspaceDto.From(tenant);
    }

    private async Task<Tenant?> ActiveBySlugAsync(string slug, CancellationToken ct)
    {
        var tenant = await tenants.GetBySlugAsync(slug, ct);
        return tenant?.Status == TenantStatus.Active ? tenant : null;
    }

    private async Task<Tenant?> UniqueActiveByNameAsync(string name, CancellationToken ct)
    {
        var matches = await tenants.ListActiveByNameAsync(name, ct);
        return matches.Count == 1 ? matches[0] : null;
    }
}
