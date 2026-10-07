using Recuro.Audit.Application.Entries;
using Recuro.Audit.Application.Entries.Commands.RecordAuditEntry;
using Recuro.Audit.Application.Entries.Queries.ListAuditEvents;
using Recuro.Audit.Application.Entries.Queries.VerifyAuditChain;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Web.Http;

namespace Recuro.Audit.Api.Endpoints;

/// <summary>
/// Audit API. There are no update or delete endpoints by design (RCU-AUD-002): entries are append-only
/// at the API and, through triggers, at the database.
/// </summary>
internal static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/audit").WithTags("Audit");

        group.MapGet("/", ListAsync)
            .RequireAuthorization(AuditPolicies.Read)
            .WithSummary("The tenant's audit trail, newest first (frontend listAudit).");

        group.MapPost("/ingest", IngestAsync)
            .RequireAuthorization(AuditPolicies.Ingest)
            .WithSummary("RCU-AUD-001: a service records a decision or state change.");

        group.MapGet("/verify", VerifyAsync)
            .RequireAuthorization(AuditPolicies.Read)
            .WithSummary("RCU-AUD-002: re-checks the tenant's hash chain since the last seal.");

        return app;
    }

    private static async Task<IResult> ListAsync(
        string? entity,
        string? actorId,
        string? correlationId,
        DateTimeOffset? from,
        DateTimeOffset? to,
        int? limit,
        IQueryHandler<ListAuditEventsQuery, IReadOnlyList<AuditEventDto>> handler,
        CancellationToken ct) =>
        (await handler.Handle(new ListAuditEventsQuery(entity, actorId, correlationId, from, to, limit), ct)).ToHttpResult();

    private static async Task<IResult> IngestAsync(
        IngestAuditEntryRequest request,
        HttpContext http,
        ICommandHandler<RecordAuditEntryCommand, AuditEventDto> handler,
        CancellationToken ct)
    {
        var command = new RecordAuditEntryCommand(
            request.Entity,
            request.Action,
            request.Before,
            request.After,
            request.Reason,
            request.ConfigVersion,
            request.ActorId,
            request.ActorName,
            request.ActorRole,
            request.OccurredAt,
            http.Connection.RemoteIpAddress?.ToString());
        return (await handler.Handle(command, ct)).ToCreatedResult(dto => $"/api/v1/audit?entity={Uri.EscapeDataString(dto.Entity)}");
    }

    private static async Task<IResult> VerifyAsync(IQueryHandler<VerifyAuditChainQuery, ChainVerificationDto> handler, CancellationToken ct) =>
        (await handler.Handle(new VerifyAuditChainQuery(), ct)).ToHttpResult();
}

/// <summary>Body of <c>POST /api/v1/audit/ingest</c>.</summary>
internal sealed record IngestAuditEntryRequest(
    string Entity,
    string Action,
    string? Before,
    string? After,
    string? Reason,
    string? ConfigVersion,
    string? ActorId,
    string? ActorName,
    string? ActorRole,
    DateTimeOffset? OccurredAt);
