namespace Recuro.Interview.Api.Http;

/// <summary>
/// Calls made while serving a request act for the signed-in user, so their bearer token goes along
/// (zero trust: each service checks it). Calls with no user (event handlers, jobs) are signed by the
/// <c>ServiceTokenHandler</c> that runs after this one (RCU-AUT-005). In Development auth mode the X-Dev-*
/// identity headers are forwarded instead; services only honour them in Development or Testing.
/// </summary>
internal sealed class ForwardIdentityHandler(IHttpContextAccessor accessor) : DelegatingHandler
{
    private static readonly string[] Headers = ["Authorization", "X-Dev-User", "X-Dev-Name", "X-Dev-Roles", "X-Dev-Tenant"];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var incoming = accessor.HttpContext?.Request.Headers;
        if (incoming is not null)
        {
            foreach (var name in Headers)
            {
                if (!request.Headers.Contains(name) && incoming.TryGetValue(name, out var value) && !string.IsNullOrEmpty(value))
                {
                    request.Headers.TryAddWithoutValidation(name, value.ToString());
                }
            }
        }

        return base.SendAsync(request, cancellationToken);
    }
}
