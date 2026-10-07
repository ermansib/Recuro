using Microsoft.AspNetCore.Authorization;
using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Domain.Tenants;

namespace Recuro.Admin.Api.Auth;

/// <summary>Tenant admins can only act while their tenant is active; suspending a tenant locks its admins out.</summary>
internal sealed class ActiveTenantRequirement : IAuthorizationRequirement;

internal sealed class ActiveTenantHandler(ITenantContext tenantContext, ITenantRepository tenants)
    : AuthorizationHandler<ActiveTenantRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ActiveTenantRequirement requirement)
    {
        if (tenantContext.TenantId is not { } tenantId)
        {
            return;
        }

        var tenant = await tenants.GetByIdAsync(tenantId, CancellationToken.None);
        if (tenant is { Status: TenantStatus.Active })
        {
            context.Succeed(requirement);
        }
    }
}
