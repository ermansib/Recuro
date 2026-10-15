using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Roles;
using Recuro.Admin.Domain.Tenants;

namespace Recuro.Admin.Application.Roles;

/// <summary>
/// People in the workspace with their roles. Accounts created directly in the identity provider (invites,
/// accounts made before roles were configurable) are imported on the way, keeping the realm roles they hold.
/// </summary>
public sealed class ListMembersHandler(
    ITenantContext tenantContext,
    ITenantRepository tenants,
    IWorkspaceRoleRepository roles,
    IWorkspaceMemberRepository members,
    IAccountDirectory accounts,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<IReadOnlyList<MemberDto>>> HandleAsync(Guid? tenantId, CancellationToken ct)
    {
        var tenant = await TenantScope.ResolveAsync(tenantContext, tenants, tenantId, ct);
        if (tenant.IsFailure)
        {
            return tenant.Error!;
        }

        var known = (await members.ListAsync(tenant.Value, ct)).ToList();
        await ImportFromDirectoryAsync(tenant.Value, known, ct);
        var list = known.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).Select(MemberDto.From).ToList();
        return Result.Success<IReadOnlyList<MemberDto>>(list);
    }

    private async Task ImportFromDirectoryAsync(Guid tenantId, List<WorkspaceMember> known, CancellationToken ct)
    {
        var byAccount = known.ToDictionary(m => m.AccountId, StringComparer.Ordinal);
        var found = await accounts.ListTenantAccountsAsync(tenantId, byAccount.Keys.ToHashSet(StringComparer.Ordinal), ct);
        if (found.IsFailure)
        {
            // The directory being down (it logs why) must not hide the people already known here.
            return;
        }

        var roleKeys = (await roles.ListAsync(tenantId, ct)).Select(r => r.Key).ToHashSet(StringComparer.Ordinal);
        var changed = false;
        foreach (var account in found.Value)
        {
            if (byAccount.TryGetValue(account.Id, out var existing))
            {
                if (existing.Name != account.Name || existing.Email != account.Email)
                {
                    existing.Rename(account.Name, account.Email);
                    changed = true;
                }

                continue;
            }

            var member = WorkspaceMember.Create(tenantId, account.Id, account.Name, account.Email, account.RealmRoles.Where(roleKeys.Contains).ToList());
            members.Add(member);
            known.Add(member);
            changed = true;
        }

        if (changed)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
    }
}

/// <summary>
/// Gives a person their workspace roles (and department). The identity provider is updated first, so a saved
/// assignment is always one the person's next sign-in carries.
/// </summary>
public sealed class UpdateMemberHandler(
    ITenantContext tenantContext,
    ITenantRepository tenants,
    IWorkspaceRoleRepository roles,
    IWorkspaceMemberRepository members,
    IAccountDirectory accounts,
    IUnitOfWork unitOfWork)
{
    public async Task<Result<MemberDto>> HandleAsync(Guid? tenantId, string accountId, UpdateMemberRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = await TenantScope.ResolveAsync(tenantContext, tenants, tenantId, ct);
        if (tenant.IsFailure)
        {
            return tenant.Error!;
        }

        var member = await members.GetAsync(tenant.Value, accountId, ct);
        if (member is null)
        {
            return WorkspaceMember.NotFound(accountId);
        }

        var workspaceRoles = await roles.ListAsync(tenant.Value, ct);
        var assigned = member.Assign(request.RoleKeys, request.Department, workspaceRoles);
        if (assigned.IsFailure)
        {
            return assigned.Error!;
        }

        var synced = await accounts.SyncWorkspaceRolesAsync(member.AccountId, member.RoleKeys, workspaceRoles.Select(r => r.Key).ToList(), ct);
        if (synced.IsFailure)
        {
            return synced.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return MemberDto.From(member);
    }
}

/// <summary>
/// A workspace admin adds a person (email isn't set up yet, so no invite link): their account is created in the
/// identity provider with a temporary password and their roles, then saved as a member. If saving fails the
/// account is removed again, so a failure never leaves an account without a member row.
/// </summary>
public sealed class CreateMemberHandler(
    ITenantContext tenantContext,
    ITenantRepository tenants,
    IWorkspaceRoleRepository roles,
    IWorkspaceMemberRepository members,
    IAccountDirectory accounts,
    IUnitOfWork unitOfWork)
{
    private const string PendingAccount = "pending";

    public async Task<Result<MemberDto>> HandleAsync(Guid? tenantId, CreateMemberRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = await TenantScope.ResolveAsync(tenantContext, tenants, tenantId, ct);
        if (tenant.IsFailure)
        {
            return tenant.Error!;
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (name.Length is 0 or > WorkspaceMember.NameMaxLength)
        {
            return MemberErrors.NameRequired;
        }

        if (!EmailAddresses.IsValid(request.Email))
        {
            return MemberErrors.InvalidEmail;
        }

        // Checks the roles and department before any account exists.
        var email = request.Email.Trim().ToLowerInvariant();
        var workspaceRoles = await roles.ListAsync(tenant.Value, ct);
        var draft = WorkspaceMember.Create(tenant.Value, PendingAccount, name, email, []);
        var assigned = draft.Assign(request.RoleKeys, request.Department, workspaceRoles);
        if (assigned.IsFailure)
        {
            return assigned.Error!;
        }

        if (!PasswordPolicy.IsSatisfiedBy(request.TemporaryPassword))
        {
            return MemberErrors.WeakPassword;
        }

        var account = await accounts.CreateWorkspacePersonAsync(
            new NewWorkspacePerson(tenant.Value, name, email, draft.RoleKeys, request.TemporaryPassword), ct);
        if (account.IsFailure)
        {
            return MemberAccountErrors.ForAdmin(account.Error!);
        }

        var member = WorkspaceMember.Create(tenant.Value, account.Value, name, email, []);
        member.Assign(draft.RoleKeys, draft.Department, workspaceRoles);
        try
        {
            members.Add(member);
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch
        {
            await accounts.DeleteAccountAsync(account.Value, CancellationToken.None);
            throw;
        }

        return MemberDto.From(member);
    }
}

/// <summary>
/// A workspace admin sets a temporary password for a person in their workspace who forgot theirs. The person must
/// choose a new one at their next sign-in. The password goes to the identity provider only; nothing is stored here.
/// </summary>
public sealed class ResetMemberPasswordHandler(
    ITenantContext tenantContext,
    ITenantRepository tenants,
    IWorkspaceMemberRepository members,
    IAccountDirectory accounts)
{
    public async Task<Result> HandleAsync(Guid? tenantId, string accountId, ResetMemberPasswordRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var tenant = await TenantScope.ResolveAsync(tenantContext, tenants, tenantId, ct);
        if (tenant.IsFailure)
        {
            return tenant.Error!;
        }

        if (await members.GetAsync(tenant.Value, accountId, ct) is null)
        {
            return WorkspaceMember.NotFound(accountId);
        }

        if (!PasswordPolicy.IsSatisfiedBy(request.TemporaryPassword))
        {
            return MemberErrors.WeakPassword;
        }

        var reset = await accounts.SetTemporaryPasswordAsync(tenant.Value, accountId, request.TemporaryPassword, ct);
        return reset.IsFailure ? MemberAccountErrors.ForAdmin(reset.Error!) : Result.Success();
    }
}

/// <summary>The account directory speaks in sign-up terms; a workspace admin gets messages written for them.</summary>
internal static class MemberAccountErrors
{
    public static Error ForAdmin(Error error)
    {
        if (error == WorkspaceErrors.EmailTaken)
        {
            return MemberErrors.EmailTaken;
        }

        if (error == WorkspaceErrors.WeakPassword)
        {
            return MemberErrors.WeakPassword;
        }

        return error == WorkspaceErrors.AccountServiceUnavailable ? MemberErrors.AccountServiceUnavailable : error;
    }
}
