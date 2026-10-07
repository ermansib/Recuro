using FluentValidation;
using Recuro.Audit.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;

namespace Recuro.Audit.Application.Entries.Queries.ListAuditEvents;

/// <summary>The tenant's audit trail, newest first. Backs the frontend's <c>listAudit()</c>.</summary>
public sealed record ListAuditEventsQuery(
    string? Entity,
    string? ActorId,
    string? CorrelationId,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int? Limit) : IQuery<IReadOnlyList<AuditEventDto>>;

internal sealed class ListAuditEventsQueryValidator : AbstractValidator<ListAuditEventsQuery>
{
    public ListAuditEventsQueryValidator()
    {
        RuleFor(q => q.Limit).InclusiveBetween(1, AuditLimits.MaxPageSize);
        RuleFor(q => q.To).GreaterThanOrEqualTo(q => q.From).When(q => q.From is not null && q.To is not null);
    }
}

internal sealed class ListAuditEventsQueryHandler(IAuditReadStore store) : IQueryHandler<ListAuditEventsQuery, IReadOnlyList<AuditEventDto>>
{
    public async Task<Result<IReadOnlyList<AuditEventDto>>> Handle(ListAuditEventsQuery query, CancellationToken ct)
    {
        var filter = new AuditFilter(
            query.Entity,
            query.ActorId,
            query.CorrelationId,
            query.From,
            query.To,
            query.Limit ?? AuditLimits.DefaultPageSize);
        return Result.Success(await store.ListAsync(filter, ct));
    }
}
