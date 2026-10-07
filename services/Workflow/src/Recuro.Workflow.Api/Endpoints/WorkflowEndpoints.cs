using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Web.Http;
using Recuro.Workflow.Application.Abstractions;
using Recuro.Workflow.Application.Approvals;
using Recuro.Workflow.Application.Workflows;

namespace Recuro.Workflow.Api.Endpoints;

/// <summary>
/// The approvals inbox (frontend listApprovals / decideApproval, reached through the gateway) and the
/// internal workflow API the domain services call (not routed by the gateway).
/// </summary>
internal static class WorkflowEndpoints
{
    public static IEndpointRouteBuilder MapWorkflowEndpoints(this IEndpointRouteBuilder app)
    {
        var approvals = app.MapGroup("/api/v1/approvals").WithTags("Approvals").RequireAuthorization(WorkflowPolicies.Approvals);

        approvals.MapGet("/", ListAsync)
            .WithSummary("RCU-WFL-007: the caller's inbox, newest first. ?status=open hides decided items.");

        approvals.MapGet("/count", CountAsync)
            .WithSummary("RCU-WFL-007: open and escalated counts for the inbox badge.");

        approvals.MapPost("/{id:guid}/decision", DecideAsync)
            .WithSummary("RCU-WFL-002: approve, reject (reason of 10+ characters) or query (pauses the SLA). A second decision is 409.");

        approvals.MapPost("/{id:guid}/resume", ResumeAsync)
            .WithSummary("RCU-WFL-004: the query was answered; the SLA clock restarts.");

        var workflows = app.MapGroup("/api/v1/workflows").WithTags("Workflows").RequireAuthorization(WorkflowPolicies.Manage);

        workflows.MapPost("/", StartAsync)
            .WithSummary("RCU-WFL-001: open a workflow on a route resolved from Config. Idempotent on correlationKey: 201 when new, 200 with the live instance otherwise.");

        workflows.MapGet("/{id:guid}", GetAsync)
            .WithSummary("One workflow instance with its tasks.");

        workflows.MapPost("/{id:guid}/cancel", CancelAsync)
            .WithSummary("Withdraw the workflow (saga compensation or a cancelled subject). Idempotent.");

        return app;
    }

    private static async Task<IResult> ListAsync(
        string? status,
        int? limit,
        IQueryHandler<ListApprovalsQuery, IReadOnlyList<ApprovalItemDto>> handler,
        CancellationToken ct) =>
        (await handler.Handle(new ListApprovalsQuery(string.Equals(status, "open", StringComparison.OrdinalIgnoreCase), limit), ct)).ToHttpResult();

    private static async Task<IResult> CountAsync(IQueryHandler<CountApprovalsQuery, InboxCount> handler, CancellationToken ct) =>
        (await handler.Handle(new CountApprovalsQuery(), ct)).ToHttpResult();

    private static async Task<IResult> DecideAsync(
        Guid id,
        DecisionRequest body,
        ICommandHandler<DecideApprovalCommand, ApprovalItemDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new DecideApprovalCommand(id, body.ActionId ?? string.Empty, body.Reason), ct)).ToHttpResult();

    private static async Task<IResult> ResumeAsync(Guid id, ICommandHandler<ResumeApprovalCommand, ApprovalItemDto> handler, CancellationToken ct) =>
        (await handler.Handle(new ResumeApprovalCommand(id), ct)).ToHttpResult();

    private static async Task<IResult> StartAsync(
        StartWorkflowRequest body,
        ICommandHandler<StartWorkflowCommand, StartedWorkflow> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new StartWorkflowCommand(body), ct);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        var instance = result.Value.Instance;
        return result.Value.Created
            ? Results.Created($"/api/v1/workflows/{instance.Id}", instance)
            : Results.Ok(instance);
    }

    private static async Task<IResult> GetAsync(Guid id, IQueryHandler<GetWorkflowQuery, WorkflowInstanceDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GetWorkflowQuery(id), ct)).ToHttpResult();

    private static async Task<IResult> CancelAsync(
        Guid id,
        CancelRequest? body,
        ICommandHandler<CancelWorkflowCommand, WorkflowInstanceDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new CancelWorkflowCommand(id, body?.Reason), ct)).ToHttpResult();

    internal sealed record DecisionRequest(string? ActionId, string? Reason);

    internal sealed record CancelRequest(string? Reason);
}
