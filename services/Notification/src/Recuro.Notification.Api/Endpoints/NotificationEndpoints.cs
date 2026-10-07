using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Web.Http;
using Recuro.Notification.Application.Emails;
using Recuro.Notification.Application.Emails.Commands;
using Recuro.Notification.Application.Emails.Queries;
using Recuro.Notification.Application.Feed;
using Recuro.Notification.Application.Feed.Commands;
using Recuro.Notification.Application.Feed.Queries;

namespace Recuro.Notification.Api.Endpoints;

/// <summary>
/// The bell and email centre (RCU-NTF-003), the delivery log (RCU-NTF-004) and bounce intake (RCU-NTF-002).
/// Shapes match the frontend's <c>AppNotification</c> and <c>EmailMessage</c>.
/// </summary>
internal static class NotificationEndpoints
{
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/notifications").WithTags("Notifications");
        var inbox = group.MapGroup(string.Empty).AddEndpointFilter<RememberContactFilter>();

        inbox.MapGet("/", ListAsync)
            .RequireAuthorization(NotificationPolicies.Inbox)
            .WithSummary("The caller's notifications, newest first (frontend listNotifications). filter=all|unread, limit, before=<id>.");

        inbox.MapGet("/unread-count", UnreadCountAsync)
            .RequireAuthorization(NotificationPolicies.Inbox)
            .WithSummary("Unread badge counts for the bell and the email centre (cached briefly).");

        inbox.MapPost("/read", MarkReadAsync)
            .RequireAuthorization(NotificationPolicies.Inbox)
            .WithSummary("Marks notifications read: { ids: [...] } or { all: true } (frontend markNotificationRead / markAllRead).");

        inbox.MapGet("/emails", ListEmailsAsync)
            .RequireAuthorization(NotificationPolicies.Inbox)
            .WithSummary("The caller's email centre, newest first (frontend listEmails).");

        inbox.MapPost("/emails/read", MarkEmailsReadAsync)
            .RequireAuthorization(NotificationPolicies.Inbox)
            .WithSummary("Marks emails read: { ids: [...] } or { all: true } (frontend markEmailRead / markAllRead).");

        group.MapGet("/delivery-log", DeliveryLogAsync)
            .RequireAuthorization(NotificationPolicies.DeliveryLog)
            .WithSummary("RCU-NTF-004: every email with template, version, recipient, timestamps, provider message id and status.");

        group.MapPost("/email-bounces", RecordBounceAsync)
            .RequireAuthorization(NotificationPolicies.Bounces)
            .WithSummary("RCU-NTF-002: the mail provider reports a hard bounce; the address stops receiving mail.");

        return app;
    }

    private static async Task<IResult> ListAsync(
        string? filter,
        int? limit,
        Guid? before,
        IQueryHandler<ListNotificationsQuery, IReadOnlyList<AppNotificationDto>> handler,
        CancellationToken ct) =>
        (await handler.Handle(new ListNotificationsQuery(filter, limit, before), ct)).ToHttpResult();

    private static async Task<IResult> UnreadCountAsync(IQueryHandler<GetUnreadCountQuery, UnreadCountDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GetUnreadCountQuery(), ct)).ToHttpResult();

    private static async Task<IResult> MarkReadAsync(MarkReadRequest request, ICommandHandler<MarkReadCommand, int> handler, CancellationToken ct) =>
        (await handler.Handle(new MarkReadCommand(ReadTarget.Notifications, request.Ids, request.All), ct)).ToHttpResult();

    private static async Task<IResult> ListEmailsAsync(
        string? filter,
        int? limit,
        Guid? before,
        IQueryHandler<ListEmailsQuery, IReadOnlyList<EmailMessageDto>> handler,
        CancellationToken ct) =>
        (await handler.Handle(new ListEmailsQuery(filter, limit, before), ct)).ToHttpResult();

    private static async Task<IResult> MarkEmailsReadAsync(MarkReadRequest request, ICommandHandler<MarkReadCommand, int> handler, CancellationToken ct) =>
        (await handler.Handle(new MarkReadCommand(ReadTarget.Emails, request.Ids, request.All), ct)).ToHttpResult();

    private static async Task<IResult> DeliveryLogAsync(
        string? status,
        string? templateKey,
        Guid? sourceEventId,
        int? limit,
        IQueryHandler<ListDeliveryLogQuery, IReadOnlyList<DeliveryLogEntryDto>> handler,
        CancellationToken ct) =>
        (await handler.Handle(new ListDeliveryLogQuery(status, templateKey, sourceEventId, limit), ct)).ToHttpResult();

    private static async Task<IResult> RecordBounceAsync(BounceRequest request, ICommandHandler<RecordBounceCommand> handler, CancellationToken ct) =>
        (await handler.Handle(new RecordBounceCommand(request.Address, request.Reason), ct)).ToHttpResult();
}

/// <summary>Body of the two read endpoints: ids, or all.</summary>
internal sealed record MarkReadRequest(IReadOnlyList<Guid>? Ids, bool All);

/// <summary>Body of <c>POST /api/v1/notifications/email-bounces</c>.</summary>
internal sealed record BounceRequest(string Address, string? Reason);
