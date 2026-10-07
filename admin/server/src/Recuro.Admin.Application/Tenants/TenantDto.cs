using Recuro.Admin.Domain.Tenants;
using Recuro.Admin.Domain.Theming;

namespace Recuro.Admin.Application.Tenants;

public sealed record TenantDto(
    Guid Id,
    string Name,
    string Slug,
    TenantKind Kind,
    TenantPlan Plan,
    TenantStatus Status,
    string? CustomDomain,
    string ThemePresetKey,
    ThemeMode ThemeMode,
    DateTimeOffset CreatedAt)
{
    public static TenantDto From(Tenant tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return new(
            tenant.Id,
            tenant.Name,
            tenant.Slug,
            tenant.Kind,
            tenant.Plan,
            tenant.Status,
            tenant.CustomDomain,
            tenant.ThemePresetKey,
            tenant.ThemeMode,
            tenant.CreatedAt);
    }
}
