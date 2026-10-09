using System.Net.Mail;
using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Tenants;

namespace Recuro.Admin.Application.Workspaces;

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
/// Creates a workspace from the public sign-up page: a row in <c>tenants</c> and the owner's account in
/// the account directory. The account is created first; if saving the workspace then fails, it is removed
/// again, so a failed sign-up never leaves an account without a workspace.
/// </summary>
public sealed class SignUpWorkspaceHandler(
    ITenantRepository tenants,
    IUnitOfWork unitOfWork,
    IAccountDirectory accounts,
    SignUpDefaults defaults,
    IClock clock)
{
    private const int MaxSlugSuffix = 1000;

    public async Task<Result<WorkspaceSignUpDto>> HandleAsync(SignUpWorkspaceRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var invalid = Validate(request);
        if (invalid is not null)
        {
            return invalid;
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var slug = await UniqueSlugAsync(request.OrgName, ct);
        var created = Tenant.SignUp(request.OrgName, slug, request.OrgType, email.Split('@')[1], defaults.Locale, defaults.Currency, clock.UtcNow);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        var tenant = created.Value;
        var ownerName = request.AdminName.Trim();
        var account = await accounts.CreateWorkspaceOwnerAsync(
            new NewWorkspaceOwner(tenant.Id, ownerName, email, request.AdminRole, request.Password), ct);
        if (account.IsFailure)
        {
            return account.Error!;
        }

        try
        {
            tenants.Add(tenant);
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            await accounts.DeleteAccountAsync(account.Value, CancellationToken.None);
            throw;
        }

        return new WorkspaceSignUpDto(WorkspaceDto.From(tenant), new WorkspaceOwnerDto(account.Value, ownerName, email, request.AdminRole));
    }

    private static Error? Validate(SignUpWorkspaceRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.OrgName) || string.IsNullOrWhiteSpace(request.AdminName))
        {
            return WorkspaceErrors.OrganisationRequired;
        }

        if (!IsValidEmail(request.Email))
        {
            return WorkspaceErrors.InvalidEmail;
        }

        if (!PasswordPolicy.IsSatisfiedBy(request.Password))
        {
            return WorkspaceErrors.WeakPassword;
        }

        if (!WorkspaceRoles.OwnerRoles.Contains(request.AdminRole))
        {
            return WorkspaceErrors.InvalidOwnerRole;
        }

        return request.AcceptTerms ? null : WorkspaceErrors.TermsNotAccepted;
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

/// <summary>Public workspace lookup, for the sign-in page of a workspace that isn't known to this browser yet.</summary>
public sealed class GetWorkspaceHandler(ITenantRepository tenants)
{
    public async Task<Result<WorkspaceDto>> HandleAsync(string slug, CancellationToken ct)
    {
        var tenant = await tenants.GetBySlugAsync(slug ?? string.Empty, ct);
        return tenant is null || tenant.Status != TenantStatus.Active
            ? WorkspaceErrors.NotFound(slug ?? string.Empty)
            : WorkspaceDto.From(tenant);
    }
}
