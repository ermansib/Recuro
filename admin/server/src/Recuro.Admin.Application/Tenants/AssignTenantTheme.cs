using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Tenants;
using Recuro.Admin.Domain.Theming;

namespace Recuro.Admin.Application.Tenants;

public sealed record AssignTenantThemeRequest(string ThemePresetKey, ThemeMode ThemeMode);

/// <summary>
/// Platform console: assigns a theme from the library to one tenant (RCU-PLT-006). The main portal picks
/// it up from the runtime config. Only presets that are available in the library can be assigned.
/// </summary>
public sealed class AssignTenantThemeHandler(ITenantRepository tenants, IThemePresetRepository presets, IUnitOfWork unitOfWork)
{
    public async Task<Result<TenantDto>> HandleAsync(Guid id, AssignTenantThemeRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var tenant = await tenants.GetByIdAsync(id, ct);
        if (tenant is null)
        {
            return TenantErrors.NotFound(id);
        }

        var preset = await presets.GetByKeyAsync(request.ThemePresetKey, ct);
        if (preset is null)
        {
            return ThemeErrors.NotFound(request.ThemePresetKey);
        }

        var applied = tenant.ApplyTheme(preset, request.ThemeMode);
        if (applied.IsFailure)
        {
            return applied.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return TenantDto.From(tenant);
    }
}
