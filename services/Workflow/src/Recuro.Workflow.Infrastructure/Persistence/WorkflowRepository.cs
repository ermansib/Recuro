using Microsoft.EntityFrameworkCore;
using Recuro.Workflow.Application.Abstractions;
using Recuro.Workflow.Domain.Workflows;

namespace Recuro.Workflow.Infrastructure.Persistence;

/// <summary>The tenant query filter on <see cref="WorkflowDbContext"/> scopes every query; tasks are auto-included.</summary>
internal sealed class WorkflowRepository(WorkflowDbContext db) : IWorkflowRepository
{
    public Task<WorkflowInstance?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Instances.FirstOrDefaultAsync(i => i.Id == id, ct);

    public Task<WorkflowInstance?> GetByTaskIdAsync(Guid taskId, CancellationToken ct) =>
        db.Instances.FirstOrDefaultAsync(i => i.Tasks.Any(t => t.Id == taskId), ct);

    public Task<WorkflowInstance?> GetLiveByCorrelationKeyAsync(string correlationKey, CancellationToken ct) =>
        db.Instances.FirstOrDefaultAsync(i => i.CorrelationKey == correlationKey && i.Status != WorkflowStatus.Cancelled, ct);

    public void Add(WorkflowInstance instance) => db.Instances.Add(instance);

    public async Task<IReadOnlyList<InboxEntry>> ListInboxAsync(IReadOnlyCollection<string> roles, bool openOnly, int limit, CancellationToken ct)
    {
        var roleList = roles.ToList();
        var tasks = db.Tasks.AsNoTracking().Where(t => roleList.Contains(t.AssigneeRole) && t.Status != ApprovalTaskStatus.Cancelled);
        if (openOnly)
        {
            tasks = tasks.Where(t => t.Status == ApprovalTaskStatus.Open);
        }

        var page = await tasks.OrderByDescending(t => t.CreatedAt).Take(limit).Select(t => new { t.Id, t.InstanceId }).ToListAsync(ct);
        var instanceIds = page.Select(p => p.InstanceId).Distinct().ToList();
        var instances = await db.Instances.AsNoTracking().Where(i => instanceIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        return page
            .Select(p => instances[p.InstanceId])
            .Select((instance, index) => new InboxEntry(instance, instance.FindTask(page[index].Id)!))
            .ToList();
    }

    public async Task<InboxCount> CountOpenAsync(IReadOnlyCollection<string> roles, CancellationToken ct)
    {
        var roleList = roles.ToList();
        var open = db.Tasks.Where(t => roleList.Contains(t.AssigneeRole) && t.Status == ApprovalTaskStatus.Open);
        return new InboxCount(await open.CountAsync(ct), await open.CountAsync(t => t.EscalationLevel > 0, ct));
    }
}
