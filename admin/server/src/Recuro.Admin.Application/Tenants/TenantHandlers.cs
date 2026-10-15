using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Application.Workspaces;
using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Tenants;

namespace Recuro.Admin.Application.Tenants;

public sealed class ListTenantsHandler(ITenantRepository tenants)
{
    public async Task<IReadOnlyList<TenantDto>> HandleAsync(CancellationToken ct) =>
        (await tenants.ListAsync(ct)).Select(TenantDto.From).ToList();
}

public sealed class GetTenantHandler(ITenantRepository tenants)
{
    public async Task<Result<TenantDto>> HandleAsync(Guid id, CancellationToken ct)
    {
        var tenant = await tenants.GetByIdAsync(id, ct);
        return tenant is null ? TenantErrors.NotFound(id) : TenantDto.From(tenant);
    }
}

/// <summary>
/// The shared create-workspace form (the same fields as the portal's sign-up page) plus the platform console's own
/// settings. The password is temporary: the owner replaces it at first sign-in.
/// </summary>
public sealed record CreateTenantRequest(
    string OrgName,
    OrgType OrgType,
    string AdminName,
    string AdminRole,
    string Email,
    string Password,
    TenantPlan Plan,
    string? CustomDomain);

/// <summary>Creates a workspace and its owner's account from the platform console.</summary>
public sealed class CreateTenantHandler(OpenWorkspaceHandler open)
{
    public async Task<Result<TenantDto>> HandleAsync(CreateTenantRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var details = new NewWorkspace(request.OrgName, request.OrgType, request.AdminName, request.AdminRole, request.Email, request.Password);
        var settings = new WorkspaceSettings(request.Plan, request.CustomDomain, OwnerMustChangePassword: true);
        var opened = await open.HandleAsync(details, settings, ct);
        return opened.IsFailure ? opened.Error! : TenantDto.From(opened.Value.Tenant);
    }
}

public sealed record UpdateTenantRequest(string Name, TenantPlan Plan, string? CustomDomain);

public sealed class UpdateTenantHandler(ITenantRepository tenants, IUnitOfWork unitOfWork)
{
    public async Task<Result<TenantDto>> HandleAsync(Guid id, UpdateTenantRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenant = await tenants.GetByIdAsync(id, ct);
        if (tenant is null)
        {
            return TenantErrors.NotFound(id);
        }

        var result = tenant.Rename(request.Name);
        if (result.IsSuccess)
        {
            result = tenant.SetCustomDomain(request.CustomDomain);
        }

        if (result.IsFailure)
        {
            return result.Error!;
        }

        tenant.ChangePlan(request.Plan);
        await unitOfWork.SaveChangesAsync(ct);
        return TenantDto.From(tenant);
    }
}

public sealed record ChangeTenantStatusRequest(TenantStatus Status);

public sealed class ChangeTenantStatusHandler(ITenantRepository tenants, IUnitOfWork unitOfWork)
{
    public async Task<Result<TenantDto>> HandleAsync(Guid id, ChangeTenantStatusRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenant = await tenants.GetByIdAsync(id, ct);
        if (tenant is null)
        {
            return TenantErrors.NotFound(id);
        }

        var result = request.Status == TenantStatus.Suspended ? tenant.Suspend() : tenant.Activate();
        if (result.IsFailure)
        {
            return result.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return TenantDto.From(tenant);
    }
}
