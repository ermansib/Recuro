using Microsoft.AspNetCore.Http;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Web.Auth;
using Serilog.Context;

namespace Recuro.BuildingBlocks.Web.Middleware;

/// <summary>Resolves tenant and user once per request from the validated token into <see cref="ScopeContext"/>.</summary>
public sealed class ScopeContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ScopeContext scope)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(scope);
        var user = context.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            var tenant = user.FindFirst(RecuroClaims.Tenant)?.Value;
            scope.SetTenant(Guid.TryParse(tenant, out var tenantId) ? tenantId : null);
            scope.SetUser(
                user.FindFirst(RecuroClaims.Subject)?.Value,
                user.FindFirst(RecuroClaims.Name)?.Value ?? user.FindFirst(RecuroClaims.PreferredUsername)?.Value,
                user.FindAll(RecuroClaims.Roles).Select(c => c.Value).ToArray());
        }

        using (LogContext.PushProperty("TenantId", scope.TenantId))
        {
            await next(context);
        }
    }
}
