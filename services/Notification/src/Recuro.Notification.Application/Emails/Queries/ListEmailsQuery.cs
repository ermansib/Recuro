using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Application.Feed;
using Recuro.Notification.Application.Feed.Queries;

namespace Recuro.Notification.Application.Emails.Queries;

/// <summary>The caller's email centre (frontend <c>listEmails</c>), newest first.</summary>
public sealed record ListEmailsQuery(string? Filter, int? Limit, Guid? Before) : IQuery<IReadOnlyList<EmailMessageDto>>;

internal sealed class ListEmailsQueryValidator : AbstractValidator<ListEmailsQuery>
{
    public ListEmailsQueryValidator()
    {
        RuleFor(q => q.Limit).InclusiveBetween(1, NotificationLimits.MaxPageSize);
        RuleFor(q => q.Filter).Must(FeedFilters.IsKnown).WithMessage("filter must be 'all' or 'unread'.");
    }
}

internal sealed class ListEmailsQueryHandler(INotificationReadStore store, ICurrentUser user)
    : IQueryHandler<ListEmailsQuery, IReadOnlyList<EmailMessageDto>>
{
    public async Task<Result<IReadOnlyList<EmailMessageDto>>> Handle(ListEmailsQuery query, CancellationToken ct)
    {
        var viewer = ViewerFrom.Current(user);
        if (viewer.IsFailure)
        {
            return viewer.Error!;
        }

        return Result.Success(await store.ListEmailsAsync(viewer.Value, new FeedFilter(FeedFilters.IsUnread(query.Filter), query.Limit ?? NotificationLimits.DefaultPageSize, query.Before), ct));
    }
}
