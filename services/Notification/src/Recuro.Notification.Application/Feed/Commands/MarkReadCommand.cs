using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Application.Feed.Queries;
using Recuro.Notification.Domain.Feed;

namespace Recuro.Notification.Application.Feed.Commands;

/// <summary>Which list a read marker applies to.</summary>
public enum ReadTarget
{
    Notifications,
    Emails,
}

/// <summary>
/// RCU-NTF-003 <c>POST /notifications/read</c>: marks the given ids, or everything, as read for the
/// caller. Ids the caller cannot see are ignored. Returns how many items changed.
/// </summary>
public sealed record MarkReadCommand(ReadTarget Target, IReadOnlyList<Guid>? Ids, bool All) : ICommand<int>;

internal sealed class MarkReadCommandValidator : AbstractValidator<MarkReadCommand>
{
    public MarkReadCommandValidator()
    {
        RuleFor(c => c.Ids)
            .NotEmpty().When(c => !c.All).WithMessage("Give ids, or set all to true.");
        RuleFor(c => c.Ids!.Count)
            .LessThanOrEqualTo(NotificationLimits.MaxIdsPerRead).When(c => c.Ids is not null).OverridePropertyName("ids");
    }
}

internal sealed class MarkReadCommandHandler(
    INotificationReadStore reads,
    INotificationStore store,
    IUnitOfWork unitOfWork,
    IUnreadCountCache cache,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<MarkReadCommand, int>
{
    public async Task<Result<int>> Handle(MarkReadCommand command, CancellationToken ct)
    {
        var viewer = ViewerFrom.Current(user);
        if (viewer.IsFailure)
        {
            return viewer.Error!;
        }

        var emails = command.Target == ReadTarget.Emails;
        var ids = command.All
            ? await reads.UnreadIdsAsync(viewer.Value, emails, ct)
            : await reads.VisibleIdsAsync(viewer.Value, command.Ids!.Distinct().ToList(), emails, ct);
        var alreadyRead = await store.ReadItemIdsAsync(viewer.Value.UserId, ids, ct);
        var now = clock.GetUtcNow();
        var marked = 0;
        foreach (var id in ids.Where(id => !alreadyRead.Contains(id)))
        {
            store.Add(ReadReceipt.For(id, viewer.Value.UserId, now));
            marked++;
        }

        if (marked > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
            await cache.InvalidateAsync(viewer.Value, ct);
        }

        return marked;
    }
}
