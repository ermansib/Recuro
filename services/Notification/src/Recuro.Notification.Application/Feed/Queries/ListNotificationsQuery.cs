using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Notification.Application.Abstractions;

namespace Recuro.Notification.Application.Feed.Queries;

/// <summary>RCU-NTF-003: the caller's bell, newest first, optionally unread only, paged by <see cref="Before"/>.</summary>
public sealed record ListNotificationsQuery(string? Filter, int? Limit, Guid? Before) : IQuery<IReadOnlyList<AppNotificationDto>>;

internal sealed class ListNotificationsQueryValidator : AbstractValidator<ListNotificationsQuery>
{
    public ListNotificationsQueryValidator()
    {
        RuleFor(q => q.Limit).InclusiveBetween(1, NotificationLimits.MaxPageSize);
        RuleFor(q => q.Filter).Must(FeedFilters.IsKnown).WithMessage("filter must be 'all' or 'unread'.");
    }
}

internal sealed class ListNotificationsQueryHandler(INotificationReadStore store, ICurrentUser user)
    : IQueryHandler<ListNotificationsQuery, IReadOnlyList<AppNotificationDto>>
{
    public async Task<Result<IReadOnlyList<AppNotificationDto>>> Handle(ListNotificationsQuery query, CancellationToken ct)
    {
        var viewer = ViewerFrom.Current(user);
        if (viewer.IsFailure)
        {
            return viewer.Error!;
        }

        var entries = await store.ListFeedAsync(viewer.Value, new FeedFilter(FeedFilters.IsUnread(query.Filter), query.Limit ?? NotificationLimits.DefaultPageSize, query.Before), ct);
        return entries.Select(e => e.Notification).ToList();
    }
}

internal static class ViewerFrom
{
    public static Result<Viewer> Current(ICurrentUser user) =>
        string.IsNullOrWhiteSpace(user.UserId)
            ? Error.Forbidden("no_user", "A signed-in user is required.")
            : new Viewer(user.UserId, user.Roles);
}
