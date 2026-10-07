using System.Globalization;
using Microsoft.Extensions.Logging;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Domain;
using Recuro.Requisition.Application.Abstractions;
using Recuro.Requisition.Domain.Requisitions;

namespace Recuro.Requisition.Application.Requisitions;

/// <summary>
/// The MRF approval saga, steps owned by this service (RCU-REQ-002, architecture.md "Sagas"):
/// resolve the DOA route in Config (pinning its version) → issue the REQ-ID → open the Workflow instance →
/// commit PendingApproval with the MRFSubmitted event. If the commit fails after the workflow opened,
/// the workflow is cancelled (compensation) and the requisition stays a Draft.
/// </summary>
internal sealed partial class RequisitionSubmission(
    IRulesClient rules,
    IWorkflowClient workflows,
    IReqIdAllocator reqIds,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<RequisitionSubmission> logger)
{
    public const string WorkflowType = "MRF";
    public const string SubjectType = "Requisition";

    public async Task<Result> SubmitAsync(ManpowerRequisition requisition, CancellationToken ct)
    {
        var ready = requisition.EnsureCanSubmit();
        if (ready.IsFailure)
        {
            return ready;
        }

        var now = clock.GetUtcNow();
        var details = requisition.Details;
        var doa = await rules.ResolveDoaAsync(details.Grade, details.OutOfBudget, now, ct);
        if (doa is null)
        {
            return Error.Validation([new FieldError("grade", "unknown_grade", $"No approval route is configured for grade {details.Grade}.")]);
        }

        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var targetClosure = await rules.AddWorkingDaysAsync(today, doa.OverallTat.MaxDays, details.Location, doa.ConfigVersionId, ct);

        if (!requisition.HasIssuedReqId)
        {
            requisition.IssueReqId(await reqIds.NextAsync(today.Year, ct));
            await unitOfWork.SaveChangesAsync(ct);
        }

        var instanceId = await workflows.StartAsync(BuildWorkflow(requisition, doa), ct);
        var plan = new SubmissionPlan(
            new ApprovalRoute(doa.Initiating, doa.Recommending, doa.Approving, doa.ApproverRole),
            doa.ConfigVersionId,
            targetClosure,
            instanceId);

        var submitted = requisition.MarkSubmitted(plan, now);
        if (submitted.IsFailure)
        {
            await CompensateAsync(requisition, instanceId, ct);
            return submitted;
        }

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await CompensateAsync(requisition, instanceId, ct);
            throw;
        }

        return Result.Success();
    }

    private async Task CompensateAsync(ManpowerRequisition requisition, Guid instanceId, CancellationToken ct)
    {
        SubmitCompensated(logger, requisition.ReqId, instanceId);
        try
        {
            await workflows.CancelAsync(instanceId, "Requisition submit did not complete; reverted to Draft.", ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The instance stays open for an operator; the requisition is still a Draft and can be resubmitted.
            CompensationFailed(logger, requisition.ReqId, instanceId, ex);
        }
    }

    /// <summary>One idempotency key per submit attempt of a given draft version, so a double-click opens one workflow.</summary>
    private static string CorrelationKey(ManpowerRequisition requisition) =>
        string.Create(CultureInfo.InvariantCulture, $"requisition:{requisition.Id:N}:{requisition.Version}");

    private static WorkflowStart BuildWorkflow(ManpowerRequisition requisition, DoaResolution doa)
    {
        var details = requisition.Details;
        var legs = doa.Legs.Count > 0
            ? doa.Legs
            : [new RouteLeg("Approving", [new RouteAssignee(doa.ApproverRole, doa.Approving)], null, [])];
        var budget = details.OutOfBudget ? "⚠ Out-of-budget" : "In-budget ✓";
        var presentation = new WorkflowPresentation(
            Kind: WorkflowType,
            Tone: details.OutOfBudget ? "dev" : "default",
            Title: $"MRF Approval — {requisition.ReqId}",
            Meta: string.Create(CultureInfo.InvariantCulture, $"{details.Designation} · {details.Grade} · {details.Location} · {details.Positions} position(s) · {budget}"),
            Route: $"Route: {doa.Initiating} → {doa.Recommending} → {doa.Approving}",
            Sensitive: new WorkflowSensitive("Band", details.Band),
            Actions:
            [
                new WorkflowAction("approve", "Approve", "primary", "resolve", "MRF approved — sourcing unlocked"),
                new WorkflowAction("reject", "Reject", "danger", "reject", null),
                new WorkflowAction("query", "Query / RFI", "ghost", "query", null),
            ]);

        return new WorkflowStart(
            WorkflowType,
            new WorkflowSubject(SubjectType, requisition.ReqId),
            doa.ConfigVersionId,
            CorrelationKey(requisition),
            legs,
            presentation);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Submit of {ReqId} did not complete; cancelling workflow {InstanceId} (compensation)")]
    private static partial void SubmitCompensated(ILogger logger, string reqId, Guid instanceId);

    [LoggerMessage(Level = LogLevel.Error, Message = "Compensation failed: workflow {InstanceId} for {ReqId} is still open and needs an operator")]
    private static partial void CompensationFailed(ILogger logger, string reqId, Guid instanceId, Exception ex);
}
