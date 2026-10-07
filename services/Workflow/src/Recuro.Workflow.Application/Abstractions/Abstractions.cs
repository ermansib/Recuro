using Recuro.Workflow.Domain.Workflows;

namespace Recuro.Workflow.Application.Abstractions;

/// <summary>Workflow instances of the current tenant, always loaded with their tasks.</summary>
public interface IWorkflowRepository
{
    Task<WorkflowInstance?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<WorkflowInstance?> GetByTaskIdAsync(Guid taskId, CancellationToken ct);

    /// <summary>The live (not cancelled) instance opened with this idempotency key, if any.</summary>
    Task<WorkflowInstance?> GetLiveByCorrelationKeyAsync(string correlationKey, CancellationToken ct);

    void Add(WorkflowInstance instance);

    /// <summary>RCU-WFL-007: tasks assigned to any of <paramref name="roles"/>, newest first.</summary>
    Task<IReadOnlyList<InboxEntry>> ListInboxAsync(IReadOnlyCollection<string> roles, bool openOnly, int limit, CancellationToken ct);

    Task<InboxCount> CountOpenAsync(IReadOnlyCollection<string> roles, CancellationToken ct);
}

public sealed record InboxEntry(WorkflowInstance Instance, ApprovalTask Task);

/// <summary>The badge count (RCU-WFL-007).</summary>
public sealed record InboxCount(int Open, int Escalated);

/// <summary>The tenant's business calendar, from the Config service (architecture.md "Synchronous contracts").</summary>
public interface IBusinessCalendar
{
    Task<DateOnly> AddWorkingDaysAsync(DateOnly from, int days, string? versionId, CancellationToken ct);
}

/// <summary>A service this one depends on did not answer. The API maps it to 503 so the client can retry.</summary>
public sealed class DependencyUnavailableException : Exception
{
    public DependencyUnavailableException()
    {
    }

    public DependencyUnavailableException(string message)
        : base(message)
    {
    }

    public DependencyUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
