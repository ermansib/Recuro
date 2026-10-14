using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Roles;

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
