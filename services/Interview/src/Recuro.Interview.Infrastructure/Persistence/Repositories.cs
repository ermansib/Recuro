using Microsoft.EntityFrameworkCore;
using Recuro.Interview.Application.Abstractions;
using Recuro.Interview.Domain.Applications;
using Recuro.Interview.Domain.Interviews;
using Recuro.Interview.Domain.Selection;

namespace Recuro.Interview.Infrastructure.Persistence;

/// <summary>The tenant query filter on <see cref="InterviewDbContext"/> scopes every query.</summary>
internal sealed class InterviewRepository(InterviewDbContext db) : IInterviewRepository
{
    public Task<InterviewRound?> GetAsync(Guid id, CancellationToken ct) =>
        db.Interviews.FirstOrDefaultAsync(i => i.Id == id, ct);

    public Task<InterviewRound?> GetByAssessmentAsync(Guid assessmentId, CancellationToken ct) =>
        db.Interviews.FirstOrDefaultAsync(i => i.Assessments.Any(a => a.Id == assessmentId), ct);

    public async Task<IReadOnlyList<InterviewRound>> ListForApplicationAsync(string appId, CancellationToken ct) =>
        await db.Interviews
            .Where(i => i.AppId == appId)
            .OrderBy(i => i.RoundNumber)
            .ThenBy(i => i.CreatedAt)
            .AsSplitQuery()
            .ToListAsync(ct);

    public async Task<IReadOnlyList<InterviewRound>> ListSlaDueAsync(DateTimeOffset now, int limit, CancellationToken ct) =>
        await db.Interviews
            .Where(i => i.Status == InterviewStatus.Scheduled
                && ((i.ReminderSentAt == null && i.ReminderAt <= now) || (i.OverdueRaisedAt == null && i.OverdueAt <= now)))
            .OrderBy(i => i.ReminderAt)
            .Take(limit)
            .AsSplitQuery()
            .ToListAsync(ct);

    public void Add(InterviewRound round) => db.Interviews.Add(round);
}

internal sealed class ApplicationTrackRepository(InterviewDbContext db) : IApplicationTrackRepository
{
    public async Task<ApplicationTrack?> GetAsync(string appId, CancellationToken ct) =>
        db.ApplicationTracks.Local.FirstOrDefault(t => t.AppId == appId)
        ?? await db.ApplicationTracks.FirstOrDefaultAsync(t => t.AppId == appId, ct);

    public void Add(ApplicationTrack track) => db.ApplicationTracks.Add(track);
}

internal sealed class SelectionRepository(InterviewDbContext db) : ISelectionRepository
{
    public Task<SelectionDecision?> GetByAppIdAsync(string appId, CancellationToken ct) =>
        db.Selections.FirstOrDefaultAsync(s => s.AppId == appId, ct);

    public Task<SelectionDecision?> GetByWorkflowAsync(Guid workflowInstanceId, CancellationToken ct) =>
        db.Selections.FirstOrDefaultAsync(s => s.WorkflowInstanceId == workflowInstanceId, ct);

    public void Add(SelectionDecision decision) => db.Selections.Add(decision);
}
