using Recuro.Admin.Application.Abstractions;
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

public sealed record CreateTenantRequest(string Name, string Slug, TenantKind Kind, TenantPlan Plan, string? CustomDomain);

public sealed class CreateTenantHandler(ITenantRepository tenants, IUnitOfWork unitOfWork, IClock clock)
{
    public async Task<Result<TenantDto>> HandleAsync(CreateTenantRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var created = Tenant.Create(request.Name, request.Slug, request.Kind, request.Plan, clock.UtcNow);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        var tenant = created.Value;
        var domain = tenant.SetCustomDomain(request.CustomDomain);
        if (domain.IsFailure)
        {
            return domain.Error!;
        }

        if (await tenants.SlugExistsAsync(tenant.Slug, ct))
        {
            return TenantErrors.SlugTaken;
        }

        tenants.Add(tenant);
        await unitOfWork.SaveChangesAsync(ct);
        return TenantDto.From(tenant);
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
