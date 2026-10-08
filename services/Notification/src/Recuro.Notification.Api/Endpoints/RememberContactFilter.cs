using Microsoft.Extensions.Caching.Memory;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Notification.Application.Directory;

namespace Recuro.Notification.Api.Endpoints;

/// <summary>
/// On inbox calls, remembers the caller's name, email and roles from their token (at most once per
/// person per <see cref="Refresh"/> on each replica), so role emails can reach them.
/// </summary>
internal sealed partial class RememberContactFilter(
    IMemoryCache seen,
    ICommandHandler<RememberContactCommand> remember,
    ITenantContext tenant,
    ILogger<RememberContactFilter> logger) : IEndpointFilter
{
    public const string EmailClaim = "email";
    private static readonly TimeSpan Refresh = TimeSpan.FromMinutes(10);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var user = context.HttpContext.User;
        var subject = user.FindFirst(RecuroClaims.Subject)?.Value;
        if (subject is not null && tenant.TenantId is { } tenantId)
        {
            var key = $"contact:{tenantId}:{subject}";
            if (!seen.TryGetValue(key, out _))
            {
                var name = user.FindFirst(RecuroClaims.Name)?.Value ?? user.FindFirst(RecuroClaims.PreferredUsername)?.Value;
                try
                {
                    await remember.Handle(new RememberContactCommand(name, user.FindFirst(EmailClaim)?.Value), context.HttpContext.RequestAborted);
                    seen.Set(key, true, Refresh);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Never fail the inbox over this; a parallel first request may have recorded the person already.
                    NotRemembered(logger, ex);
                }
            }
        }

        return await next(context);
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Could not record the caller's contact details")]
    private static partial void NotRemembered(ILogger logger, Exception ex);
}
