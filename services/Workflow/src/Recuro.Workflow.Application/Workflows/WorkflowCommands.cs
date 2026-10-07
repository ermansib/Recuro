using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Workflow.Application.Abstractions;
using Recuro.Workflow.Application.Approvals;
using Recuro.Workflow.Domain.Workflows;

namespace Recuro.Workflow.Application.Workflows;

/// <summary>Body of <c>POST /api/v1/workflows</c> (RCU-WFL-001). The calling service resolved the route from Config.</summary>
public sealed record StartWorkflowRequest(
    string? Type,
    SubjectInput? Subject,
    string? ConfigVersionId,
    string? CorrelationKey,
    IReadOnlyList<LegInput>? Legs,
    PresentationInput? Presentation);

public sealed record SubjectInput(string? Type, string? Id);

public sealed record LegInput(string? Name, IReadOnlyList<AssigneeInput>? Assignees, int? SlaWorkingDays, IReadOnlyList<EscalationInput>? Escalation);

public sealed record AssigneeInput(string? Role, string? Label);

public sealed record EscalationInput(string? Role, string? Label, int AfterWorkingDays);

public sealed record PresentationInput(
    string? Kind,
    string? Tone,
    string? Title,
    string? Meta,
    string? Route,
    SensitiveDto? Sensitive,
    IReadOnlyList<ActionInput>? Actions,
    ChipDto? Chip);

public sealed record ActionInput(string? Id, string? Label, string? Style, string? Effect, string? ResultText);

/// <summary>An instance as the owning service sees it.</summary>
public sealed record WorkflowInstanceDto(
    Guid Id,
    string Type,
    EntityRefDto Subject,
    WorkflowStatus Status,
    string ConfigVersionId,
    int CurrentLeg,
    IReadOnlyList<WorkflowTaskDto> Tasks)
{
    public static WorkflowInstanceDto From(WorkflowInstance instance)
    {
        ArgumentNullException.ThrowIfNull(instance);
        return new WorkflowInstanceDto(
            instance.Id,
            instance.Type,
            new EntityRefDto(instance.SubjectType, instance.SubjectId),
            instance.Status,
            instance.ConfigVersionId,
            instance.CurrentLeg,
            instance.Tasks.Select(t => new WorkflowTaskDto(t.Id, t.LegIndex, t.AssigneeRole, t.Status, t.DueAt, t.EscalationLevel, t.Decision?.ActionId)).ToList());
    }
}

public sealed record WorkflowTaskDto(Guid Id, int Leg, string AssigneeRole, ApprovalTaskStatus Status, DateTimeOffset? DueAt, int EscalationLevel, string? DecisionActionId);

/// <summary>Result of a start: <see cref="Created"/> is false when the idempotency key matched a live instance.</summary>
public sealed record StartedWorkflow(WorkflowInstanceDto Instance, bool Created);

public sealed record StartWorkflowCommand(StartWorkflowRequest Request) : ICommand<StartedWorkflow>;

internal sealed class StartWorkflowCommandValidator : AbstractValidator<StartWorkflowCommand>
{
    public const int Short = 200;
    public const int Long = 2000;

    public StartWorkflowCommandValidator()
    {
        RuleFor(c => c.Request).NotNull();
        RuleFor(c => c.Request.Type).NotEmpty().MaximumLength(50).OverridePropertyName("type");
        RuleFor(c => c.Request.Subject).NotNull().OverridePropertyName("subject");
        RuleFor(c => c.Request.Subject!.Type).NotEmpty().MaximumLength(50).OverridePropertyName("subject.type").When(c => c.Request.Subject is not null);
        RuleFor(c => c.Request.Subject!.Id).NotEmpty().MaximumLength(100).OverridePropertyName("subject.id").When(c => c.Request.Subject is not null);
        RuleFor(c => c.Request.ConfigVersionId).NotEmpty().MaximumLength(100).OverridePropertyName("configVersionId");
        RuleFor(c => c.Request.CorrelationKey).NotEmpty().MaximumLength(200).OverridePropertyName("correlationKey");
        RuleFor(c => c.Request.Legs).NotEmpty().Must(l => l!.Count <= 10).OverridePropertyName("legs");
        RuleForEach(c => c.Request.Legs).ChildRules(leg =>
        {
            leg.RuleFor(l => l.Name).NotEmpty().MaximumLength(Short);
            leg.RuleFor(l => l.Assignees).NotEmpty().Must(a => a!.Count <= 10);
            leg.RuleForEach(l => l.Assignees).ChildRules(a =>
            {
                a.RuleFor(x => x.Role).NotEmpty().MaximumLength(50);
                a.RuleFor(x => x.Label).MaximumLength(Short);
            });
            leg.RuleFor(l => l.SlaWorkingDays).InclusiveBetween(1, 365);
            leg.RuleFor(l => l.Escalation).Must(e => e is null || e.Count <= 5);
            leg.RuleForEach(l => l.Escalation).ChildRules(e =>
            {
                e.RuleFor(x => x.Role).NotEmpty().MaximumLength(50);
                e.RuleFor(x => x.Label).MaximumLength(Short);
                e.RuleFor(x => x.AfterWorkingDays).InclusiveBetween(0, 365);
            });
            leg.RuleFor(l => l.Escalation).Empty().When(l => l.SlaWorkingDays is null)
                .WithMessage("Escalation needs an SLA to count from.");
        }).OverridePropertyName("legs");
        RuleFor(c => c.Request.Presentation).NotNull().OverridePropertyName("presentation");
        When(c => c.Request.Presentation is not null, () =>
        {
            RuleFor(c => c.Request.Presentation!.Kind).NotEmpty().MaximumLength(50).OverridePropertyName("presentation.kind");
            RuleFor(c => c.Request.Presentation!.Tone).NotEmpty().MaximumLength(20).OverridePropertyName("presentation.tone");
            RuleFor(c => c.Request.Presentation!.Title).NotEmpty().MaximumLength(Short).OverridePropertyName("presentation.title");
            RuleFor(c => c.Request.Presentation!.Meta).MaximumLength(Long).OverridePropertyName("presentation.meta");
            RuleFor(c => c.Request.Presentation!.Route).MaximumLength(Long).OverridePropertyName("presentation.route");
            RuleFor(c => c.Request.Presentation!.Actions).NotEmpty().Must(a => a!.Count <= 6).OverridePropertyName("presentation.actions");
            RuleForEach(c => c.Request.Presentation!.Actions).ChildRules(a =>
            {
                a.RuleFor(x => x.Id).NotEmpty().MaximumLength(50);
                a.RuleFor(x => x.Label).NotEmpty().MaximumLength(Short);
                a.RuleFor(x => x.Style).Must(s => s is "primary" or "danger" or "ghost");
                a.RuleFor(x => x.Effect).Must(e => ApprovalItemMapper.ParseEffect(e) is not null).WithMessage("effect must be resolve, reject or query.");
                a.RuleFor(x => x.ResultText).MaximumLength(Short);
            }).OverridePropertyName("presentation.actions");
        });
    }
}

internal sealed class StartWorkflowCommandHandler(
    IWorkflowRepository workflows,
    SlaPlanner sla,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<StartWorkflowCommand, StartedWorkflow>
{
    public async Task<Result<StartedWorkflow>> Handle(StartWorkflowCommand command, CancellationToken ct)
    {
        var r = command.Request;
        var existing = await workflows.GetLiveByCorrelationKeyAsync(r.CorrelationKey!, ct);
        if (existing is not null)
        {
            return new StartedWorkflow(WorkflowInstanceDto.From(existing), Created: false);
        }

        var legs = r.Legs!.Select(ToLeg).ToList();
        var now = clock.GetUtcNow();
        var schedule = await sla.ScheduleAsync(legs[0], r.ConfigVersionId!, now, ct);
        var started = WorkflowInstance.Start(
            r.Type!,
            r.Subject!.Type!,
            r.Subject.Id!,
            r.CorrelationKey!,
            r.ConfigVersionId!,
            legs,
            ToPresentation(r.Presentation!),
            new Actor(user.UserId ?? string.Empty, user.Name ?? user.UserId ?? string.Empty, user.Roles),
            schedule,
            now);
        if (started.IsFailure)
        {
            return started.Error!;
        }

        workflows.Add(started.Value);
        await unitOfWork.SaveChangesAsync(ct);
        return new StartedWorkflow(WorkflowInstanceDto.From(started.Value), Created: true);
    }

    private static LegDefinition ToLeg(LegInput leg) => new(
        leg.Name!,
        leg.Assignees!.Select(a => new LegAssignee(a.Role!, a.Label ?? a.Role!)).ToList(),
        leg.SlaWorkingDays,
        (leg.Escalation ?? []).Select(e => new LegEscalation(e.Role!, e.Label ?? e.Role!, e.AfterWorkingDays)).ToList());

    private static Presentation ToPresentation(PresentationInput p) => new(
        p.Kind!,
        p.Tone!,
        p.Title!,
        p.Meta ?? string.Empty,
        p.Route ?? string.Empty,
        p.Sensitive is null ? null : new SensitiveValue(p.Sensitive.Label, p.Sensitive.Value),
        p.Actions!.Select(a => new InboxAction(a.Id!, a.Label!, a.Style!, ApprovalItemMapper.ParseEffect(a.Effect)!.Value, a.ResultText)).ToList(),
        p.Chip is null ? null : new InboxChip(p.Chip.Text, p.Chip.Tone));
}

/// <summary>The owning service withdraws its request (saga compensation, or its subject was cancelled).</summary>
public sealed record CancelWorkflowCommand(Guid Id, string? Reason) : ICommand<WorkflowInstanceDto>;

internal sealed class CancelWorkflowCommandValidator : AbstractValidator<CancelWorkflowCommand>
{
    public CancelWorkflowCommandValidator() => RuleFor(c => c.Reason).MaximumLength(StartWorkflowCommandValidator.Long);
}

internal sealed class CancelWorkflowCommandHandler(IWorkflowRepository workflows, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<CancelWorkflowCommand, WorkflowInstanceDto>
{
    public async Task<Result<WorkflowInstanceDto>> Handle(CancelWorkflowCommand command, CancellationToken ct)
    {
        var instance = await workflows.GetByIdAsync(command.Id, ct);
        if (instance is null)
        {
            return WorkflowErrors.InstanceNotFound(command.Id);
        }

        var result = instance.Cancel(command.Reason ?? "Withdrawn by the owning service.", clock.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return WorkflowInstanceDto.From(instance);
    }
}

public sealed record GetWorkflowQuery(Guid Id) : IQuery<WorkflowInstanceDto>;

internal sealed class GetWorkflowQueryHandler(IWorkflowRepository workflows) : IQueryHandler<GetWorkflowQuery, WorkflowInstanceDto>
{
    public async Task<Result<WorkflowInstanceDto>> Handle(GetWorkflowQuery query, CancellationToken ct)
    {
        var instance = await workflows.GetByIdAsync(query.Id, ct);
        return instance is null ? WorkflowErrors.InstanceNotFound(query.Id) : WorkflowInstanceDto.From(instance);
    }
}
