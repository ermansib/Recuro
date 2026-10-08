using FluentValidation;
using Microsoft.Extensions.Options;
using Recuro.Bgv.Application.Abstractions;
using Recuro.Bgv.Domain.Cases;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;

namespace Recuro.Bgv.Application.Cases.Commands;

/// <summary>
/// RCU-BGV-004/006 (frontend <c>reportAdverseFinding</c>): HR-TA flags a check. The offer release locks,
/// Pipeline holds the application (<c>bgv.adverse.flagged</c>) and a Workflow instance escalates the
/// finding to HR Head + Compliance, then MD/CEO, whose rescind/override decision closes the saga.
/// </summary>
public sealed record ReportAdverseFindingCommand(string CaseRef, string Check, string Description, string Action) : ICommand<BgvCaseDto>;

internal sealed class ReportAdverseFindingCommandValidator : AbstractValidator<ReportAdverseFindingCommand>
{
    public ReportAdverseFindingCommandValidator()
    {
        RuleFor(c => c.Check).NotEmpty().MaximumLength(BgvLimits.CheckTypeLength);
        RuleFor(c => c.Description).NotEmpty().WithErrorCode("description_required").MaximumLength(BgvLimits.DescriptionLength);
        RuleFor(c => c.Action).IsEnumName(typeof(AdverseAction), caseSensitive: true)
            .WithMessage($"Action must be one of: {string.Join(", ", Enum.GetNames<AdverseAction>())}.");
    }
}

internal sealed class ReportAdverseFindingCommandHandler(
    IBgvCaseRepository cases,
    IBgvRules rules,
    IWorkflowClient workflows,
    ISensitiveNotePolicy sensitiveNotes,
    ICurrentUser caller,
    IUnitOfWork unitOfWork,
    IOptions<BgvOptions> options,
    TimeProvider clock) : ICommandHandler<ReportAdverseFindingCommand, BgvCaseDto>
{
    public async Task<Result<BgvCaseDto>> Handle(ReportAdverseFindingCommand command, CancellationToken ct)
    {
        var bgvCase = await cases.FindAsync(command.CaseRef, ct);
        if (bgvCase is null)
        {
            return BgvErrors.NotFound(command.CaseRef);
        }

        var now = clock.GetUtcNow();
        var reported = bgvCase.ReportAdverse(command.Check, command.Description, Enum.Parse<AdverseAction>(command.Action), Actors.From(caller), now);
        if (reported.IsFailure)
        {
            return reported.Error!;
        }

        // Open the escalation before committing: the call is idempotent on the correlation key, so a retry
        // after a failed save finds the same instance instead of opening a second one.
        var route = await rules.GetAdverseEscalationAsync(ct);
        var workflowId = await workflows.StartAsync(AdverseEscalation.For(bgvCase, route, options.Value.AdverseLegSlaWorkingDays), ct);
        bgvCase.AttachEscalation(workflowId);

        await unitOfWork.SaveChangesAsync(ct);
        return BgvCaseDto.From(bgvCase, now, await sensitiveNotes.CallerMaySeeAsync(ct));
    }
}

/// <summary>
/// The adverse-BGV workflow (FRD §5.4 escalation "adverse-bgv"): first HR Head + Compliance, final MD/CEO.
/// Inbox actions match the frontend mock; rescind and override both need a documented reason.
/// </summary>
public static class AdverseEscalation
{
    public const string WorkflowType = "bgv-adverse";
    public const string SubjectType = "BgvCase";
    public const string EscalateAction = "escalate";
    public const string ClarifyAction = "clarify";
    public const string RescindAction = "rescind";
    public const string OverrideAction = "override";

    public static WorkflowStart For(BgvCase bgvCase, EscalationRoute route, int legSlaWorkingDays)
    {
        ArgumentNullException.ThrowIfNull(bgvCase);
        ArgumentNullException.ThrowIfNull(route);
        var finding = bgvCase.Adverse ?? throw new InvalidOperationException("The case has no adverse finding.");
        var check = bgvCase.Checks.First(c => c.Type == finding.Check);
        return new WorkflowStart(
            WorkflowType,
            new WorkflowSubject(SubjectType, bgvCase.Id.ToString()),
            route.ConfigVersionId,
            $"{WorkflowType}:{bgvCase.Id}:{finding.Check}",
            [
                new WorkflowLeg("First escalation", [new WorkflowAssignee(route.First.Role, route.First.Label)], legSlaWorkingDays, []),
                new WorkflowLeg("Final decision", [new WorkflowAssignee(route.Final.Role, route.Final.Label)], legSlaWorkingDays, []),
            ],
            new WorkflowPresentation(
                "AdverseBgv",
                "adverse",
                $"⚑ Adverse BGV — {bgvCase.AppId} ({bgvCase.ReqId})",
                $"{check.Label}: {finding.Description}",
                $"First escalation: {route.First.Label} → final: {route.Final.Label}. Offer ON HOLD. HR-TA recommends: {finding.Action}.",
                [
                    new WorkflowAction(EscalateAction, $"Escalate to {route.Final.Label}", "danger", "resolve", $"Escalated to {route.Final.Label} with Compliance note"),
                    new WorkflowAction(ClarifyAction, "Seek Vendor Clarification", "ghost", "query", null),
                    new WorkflowAction(RescindAction, "Accept — Rescind", "danger", "reject", null),
                    new WorkflowAction(OverrideAction, "Override — Proceed", "ghost", "reject", null),
                ],
                new WorkflowChip(check.Label, "red")));
    }
}
