using FluentValidation;
using Microsoft.Extensions.Logging;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Requisition.Application.Abstractions;
using Recuro.Requisition.Domain.Requisitions;

namespace Recuro.Requisition.Application.Requisitions.Queries;

/// <summary>RCU-REQ-007: the tracker list (frontend <c>listRequisitions</c>), newest first.</summary>
public sealed record ListRequisitionsQuery(RequisitionState? State, string? Grade, string? Location, string? Q, int? Limit)
    : IQuery<IReadOnlyList<RequisitionDto>>
{
    public const int DefaultLimit = 200;
    public const int MaxLimit = 500;
}

internal sealed class ListRequisitionsQueryValidator : AbstractValidator<ListRequisitionsQuery>
{
    public ListRequisitionsQueryValidator()
    {
        RuleFor(q => q.Limit).InclusiveBetween(1, ListRequisitionsQuery.MaxLimit);
        RuleFor(q => q.State).IsInEnum();
        RuleFor(q => q.Q).MaximumLength(RequisitionLimits.ShortText);
    }
}

internal sealed class ListRequisitionsQueryHandler(IRequisitionRepository requisitions, TimeProvider clock)
    : IQueryHandler<ListRequisitionsQuery, IReadOnlyList<RequisitionDto>>
{
    public async Task<Result<IReadOnlyList<RequisitionDto>>> Handle(ListRequisitionsQuery query, CancellationToken ct)
    {
        var filter = new RequisitionFilter(query.State, query.Grade, query.Location, query.Q, query.Limit ?? ListRequisitionsQuery.DefaultLimit);
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var rows = await requisitions.ListAsync(filter, ct);
        return rows.Select(r => RequisitionDto.From(r, today)).ToList();
    }
}

public sealed record GetRequisitionQuery(string ReqId) : IQuery<VersionedRequisition>;

internal sealed class GetRequisitionQueryHandler(IRequisitionRepository requisitions, TimeProvider clock)
    : IQueryHandler<GetRequisitionQuery, VersionedRequisition>
{
    public async Task<Result<VersionedRequisition>> Handle(GetRequisitionQuery query, CancellationToken ct)
    {
        var requisition = await requisitions.GetByReqIdAsync(query.ReqId, ct);
        return requisition is null
            ? RequisitionErrors.NotFound(query.ReqId)
            : new VersionedRequisition(RequisitionDto.From(requisition, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime)), requisition.Version);
    }
}

/// <summary>RCU-REQ-005: may candidates enter this requisition? Pipeline, Careers and Employee call it before intake.</summary>
public sealed record GetSourcingGateQuery(string ReqId) : IQuery<SourcingGateDto>;

public sealed record SourcingGateDto(string ReqId, bool Allowed, RequisitionState State);

internal sealed partial class GetSourcingGateQueryHandler(IRequisitionRepository requisitions, ILogger<GetSourcingGateQueryHandler> logger)
    : IQueryHandler<GetSourcingGateQuery, SourcingGateDto>
{
    public async Task<Result<SourcingGateDto>> Handle(GetSourcingGateQuery query, CancellationToken ct)
    {
        var requisition = await requisitions.GetByReqIdAsync(query.ReqId, ct);
        if (requisition is null)
        {
            return RequisitionErrors.NotFound(query.ReqId);
        }

        if (!requisition.SourcingAllowed)
        {
            GateClosed(logger, requisition.ReqId, requisition.State);
        }

        return new SourcingGateDto(requisition.ReqId, requisition.SourcingAllowed, requisition.State);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Sourcing gate closed for {ReqId} ({State})")]
    private static partial void GateClosed(ILogger logger, string reqId, RequisitionState state);
}
