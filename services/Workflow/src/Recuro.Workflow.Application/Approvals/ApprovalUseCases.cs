using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Workflow.Application.Abstractions;
using Recuro.Workflow.Application.Workflows;
using Recuro.Workflow.Domain.Workflows;

namespace Recuro.Workflow.Application.Approvals;

/// <summary>RCU-WFL-007: the caller's approvals inbox (frontend <c>listApprovals</c>), newest first.</summary>
public sealed record ListApprovalsQuery(bool OpenOnly, int? Limit) : IQuery<IReadOnlyList<ApprovalItemDto>>
{
    public const int DefaultLimit = 200;
    public const int MaxLimit = 500;
}

internal sealed class ListApprovalsQueryValidator : AbstractValidator<ListApprovalsQuery>
{
    public ListApprovalsQueryValidator() => RuleFor(q => q.Limit).InclusiveBetween(1, ListApprovalsQuery.MaxLimit);
}

internal sealed class ListApprovalsQueryHandler(IWorkflowRepository workflows, ICurrentUser user, TimeProvider clock)
    : IQueryHandler<ListApprovalsQuery, IReadOnlyList<ApprovalItemDto>>
{
    public async Task<Result<IReadOnlyList<ApprovalItemDto>>> Handle(ListApprovalsQuery query, CancellationToken ct)
    {
        var entries = await workflows.ListInboxAsync(user.Roles, query.OpenOnly, query.Limit ?? ListApprovalsQuery.DefaultLimit, ct);
        var now = clock.GetUtcNow();
        return entries.Select(e => ApprovalItemMapper.From(e.Instance, e.Task, user.Roles, now)).ToList();
    }
}

/// <summary>RCU-WFL-007: the inbox badge.</summary>
public sealed record CountApprovalsQuery : IQuery<InboxCount>;

internal sealed class CountApprovalsQueryHandler(IWorkflowRepository workflows, ICurrentUser user) : IQueryHandler<CountApprovalsQuery, InboxCount>
{
    public async Task<Result<InboxCount>> Handle(CountApprovalsQuery query, CancellationToken ct) =>
        await workflows.CountOpenAsync(user.Roles, ct);
}

/// <summary>RCU-WFL-002 / RCU-APR: decide an inbox item (frontend <c>decideApproval</c>).</summary>
public sealed record DecideApprovalCommand(Guid TaskId, string ActionId, string? Reason) : ICommand<ApprovalItemDto>;

internal sealed class DecideApprovalCommandValidator : AbstractValidator<DecideApprovalCommand>
{
    public DecideApprovalCommandValidator()
    {
        RuleFor(c => c.ActionId).NotEmpty().MaximumLength(50);
        RuleFor(c => c.Reason).MaximumLength(StartWorkflowCommandValidator.Long);
    }
}

internal sealed class DecideApprovalCommandHandler(
    IWorkflowRepository workflows,
    SlaPlanner sla,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<DecideApprovalCommand, ApprovalItemDto>
{
    public async Task<Result<ApprovalItemDto>> Handle(DecideApprovalCommand command, CancellationToken ct)
    {
        var instance = await workflows.GetByTaskIdAsync(command.TaskId, ct);
        if (instance is null)
        {
            return WorkflowErrors.TaskNotFound(command.TaskId);
        }

        var now = clock.GetUtcNow();
        var nextLeg = OpensNextLeg(instance, command) ? await sla.ScheduleAsync(instance.NextLeg, instance.ConfigVersionId, now, ct) : TaskSchedule.None;
        var actor = new Actor(user.UserId ?? string.Empty, user.Name ?? user.UserId ?? string.Empty, user.Roles);
        var result = instance.Decide(command.TaskId, actor, command.ActionId, command.Reason, nextLeg, now);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return ApprovalItemMapper.From(instance, instance.FindTask(command.TaskId)!, user.Roles, now);
    }

    /// <summary>Deadlines for the next leg are needed only when this approval is the last one open on the current leg.</summary>
    private static bool OpensNextLeg(WorkflowInstance instance, DecideApprovalCommand command)
    {
        var action = instance.Presentation.Actions.FirstOrDefault(a => a.Id == command.ActionId);
        return instance.NextLeg is not null
            && action?.Effect == ActionEffect.Resolve
            && instance.Tasks.Where(t => t.LegIndex == instance.CurrentLeg && t.Id != command.TaskId).All(t => t.Status == ApprovalTaskStatus.Completed);
    }
}

/// <summary>RCU-WFL-004: the initiator answered a query; the SLA clock restarts.</summary>
public sealed record ResumeApprovalCommand(Guid TaskId) : ICommand<ApprovalItemDto>;

internal sealed class ResumeApprovalCommandHandler(
    IWorkflowRepository workflows,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<ResumeApprovalCommand, ApprovalItemDto>
{
    public async Task<Result<ApprovalItemDto>> Handle(ResumeApprovalCommand command, CancellationToken ct)
    {
        var instance = await workflows.GetByTaskIdAsync(command.TaskId, ct);
        if (instance is null)
        {
            return WorkflowErrors.TaskNotFound(command.TaskId);
        }

        var now = clock.GetUtcNow();
        var result = instance.Resume(command.TaskId, now);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return ApprovalItemMapper.From(instance, instance.FindTask(command.TaskId)!, user.Roles, now);
    }
}
