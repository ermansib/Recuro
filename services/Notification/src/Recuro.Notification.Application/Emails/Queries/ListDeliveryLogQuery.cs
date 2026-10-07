using FluentValidation;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Application.Feed;
using Recuro.Notification.Domain.Emails;

namespace Recuro.Notification.Application.Emails.Queries;

/// <summary>RCU-NTF-004: the tenant's email delivery log, newest first.</summary>
public sealed record ListDeliveryLogQuery(string? Status, string? TemplateKey, Guid? SourceEventId, int? Limit) : IQuery<IReadOnlyList<DeliveryLogEntryDto>>;

internal sealed class ListDeliveryLogQueryValidator : AbstractValidator<ListDeliveryLogQuery>
{
    public ListDeliveryLogQueryValidator()
    {
        RuleFor(q => q.Limit).InclusiveBetween(1, NotificationLimits.MaxPageSize);
        RuleFor(q => q.Status)
            .Must(s => s is null || Enum.TryParse<EmailStatus>(s, ignoreCase: true, out _))
            .WithMessage($"status must be one of: {string.Join(", ", Enum.GetNames<EmailStatus>())}.");
    }
}

internal sealed class ListDeliveryLogQueryHandler(INotificationReadStore store) : IQueryHandler<ListDeliveryLogQuery, IReadOnlyList<DeliveryLogEntryDto>>
{
    public async Task<Result<IReadOnlyList<DeliveryLogEntryDto>>> Handle(ListDeliveryLogQuery query, CancellationToken ct) =>
        Result.Success(await store.ListDeliveryLogAsync(
            new DeliveryLogFilter(query.Status, query.TemplateKey, query.SourceEventId, query.Limit ?? NotificationLimits.DefaultPageSize), ct));
}
