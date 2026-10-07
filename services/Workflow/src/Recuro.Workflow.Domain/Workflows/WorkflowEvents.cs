using Recuro.BuildingBlocks.Domain;

namespace Recuro.Workflow.Domain.Workflows;

public sealed record TaskCreated(WorkflowInstance Instance, ApprovalTask Task) : IDomainEvent;

public sealed record TaskCompleted(WorkflowInstance Instance, ApprovalTask Task) : IDomainEvent;

public sealed record TaskEscalated(WorkflowInstance Instance, ApprovalTask Task, EscalationStep Step) : IDomainEvent;

public sealed record SlaPaused(WorkflowInstance Instance, ApprovalTask Task) : IDomainEvent;

public sealed record SlaResumed(WorkflowInstance Instance, ApprovalTask Task) : IDomainEvent;
