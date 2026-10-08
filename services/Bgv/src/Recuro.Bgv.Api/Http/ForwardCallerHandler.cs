using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.Bgv.Api.Http;

/// <summary>
/// Calls to other services carry the caller's own credentials (zero trust: the callee authorises and
/// masks for the real caller). In Development auth mode the X-Dev-* headers play the token's part.
/// </summary>
internal sealed class ForwardCallerHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    private static readonly string[] Forwarded =
    [
        "Authorization",
        DevelopmentAuthenticationHandler.UserHeader,
        DevelopmentAuthenticationHandler.NameHeader,
        DevelopmentAuthenticationHandler.RolesHeader,
        DevelopmentAuthenticationHandler.TenantHeader,
    ];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var headers = accessor.HttpContext?.Request.Headers;
        if (headers is not null)
        {
            foreach (var name in Forwarded)
            {
                if (headers.TryGetValue(name, out var value) && !request.Headers.Contains(name))
                {
                    request.Headers.TryAddWithoutValidation(name, value.ToString());
                }
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
