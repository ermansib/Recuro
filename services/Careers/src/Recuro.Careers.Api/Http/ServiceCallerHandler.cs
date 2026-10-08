using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Careers.Application.Abstractions;

namespace Recuro.Careers.Api.Http;

/// <summary>
/// Signs intake calls (Candidate, Pipeline) as this service rather than the caller: a public applicant
/// has no token, and the target services grant creation to the <c>service</c> role, whose masking map
/// applies. Stand-in for BuildingBlocks' <c>ServiceTokenHandler</c> (RCU-AUT-005, not merged yet), with
/// the same rules: requests already carrying credentials are left alone, and in
/// <c>Auth:Mode=Development</c> it sends <c>X-Dev-*</c> headers for <c>recuro-svc-careers</c> and the
/// tenant in scope. In OIDC mode it refuses (503) until client-credentials tokens exist; swap this
/// class for <c>ServiceTokenHandler</c> when that lands.
/// </summary>
internal sealed class ServiceCallerHandler(IHttpContextAccessor accessor, IOptions<RecuroAuthOptions> auth) : DelegatingHandler
{
    public const string ClientId = "recuro-svc-careers";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Headers.Authorization is not null || request.Headers.Contains(DevelopmentAuthenticationHandler.UserHeader))
        {
            return base.SendAsync(request, cancellationToken);
        }

        if (!auth.Value.IsDevelopmentMode)
        {
            throw new DependencyUnavailableException("Service-to-service tokens (RCU-AUT-005) are not configured for this service yet.");
        }

        var tenant = accessor.HttpContext?.RequestServices.GetService<ITenantContext>()?.TenantId;
        request.Headers.TryAddWithoutValidation(DevelopmentAuthenticationHandler.UserHeader, ClientId);
        request.Headers.TryAddWithoutValidation(DevelopmentAuthenticationHandler.RolesHeader, RecuroRoles.Service);
        if (tenant is { } id)
        {
            request.Headers.TryAddWithoutValidation(DevelopmentAuthenticationHandler.TenantHeader, id.ToString());
        }

        return base.SendAsync(request, cancellationToken);
    }
}
