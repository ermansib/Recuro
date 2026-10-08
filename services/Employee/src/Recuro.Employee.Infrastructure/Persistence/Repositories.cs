using Microsoft.EntityFrameworkCore;
using Recuro.Employee.Application.Abstractions;
using Recuro.Employee.Domain.Ijp;
using Recuro.Employee.Domain.Intake;
using Recuro.Employee.Domain.Referrals;
using Recuro.Employee.Domain.Requisitions;

namespace Recuro.Employee.Infrastructure.Persistence;

/// <summary>The tenant query filter on <see cref="EmployeeDbContext"/> scopes every query.</summary>
internal sealed class IjpPostingRepository(EmployeeDbContext db) : IIjpPostingRepository
{
    public Task<IjpPosting?> GetAsync(string reqId, CancellationToken ct) =>
        db.IjpPostings.FirstOrDefaultAsync(p => p.ReqId == reqId, ct);

    public async Task<IReadOnlyList<IjpPosting>> ListOpenAsync(DateTimeOffset now, CancellationToken ct) =>
        await db.IjpPostings.AsNoTracking()
            .Where(p => !p.Withdrawn && p.OpensAt <= now && p.ClosesAt >= now)
            .OrderBy(p => p.ClosesAt)
            .ToListAsync(ct);

    public void Add(IjpPosting posting) => db.IjpPostings.Add(posting);
}

internal sealed class IntakeRepository(EmployeeDbContext db) : IIntakeRepository
{
    public async Task<IReadOnlyList<InternalApplication>> ListApplicationsAsync(string employeeId, CancellationToken ct) =>
        await db.InternalApplications.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId)
            .OrderByDescending(a => a.SubmittedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<Referral>> ListReferralsAsync(string employeeId, CancellationToken ct) =>
        await db.Referrals.AsNoTracking()
            .Where(r => r.EmployeeId == employeeId)
            .OrderByDescending(r => r.SubmittedAt)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<InternalApplication>> ListApplicationsAsync(string employeeId, string reqId, CancellationToken ct) =>
        await db.InternalApplications.AsNoTracking()
            .Where(a => a.EmployeeId == employeeId && a.ReqId == reqId)
            .ToListAsync(ct);

    public Task<IntakeRecord?> GetByAppIdAsync(string appId, CancellationToken ct) =>
        db.IntakeRecords.FirstOrDefaultAsync(r => r.AppId == appId, ct);

    public void Add(IntakeRecord record) => db.IntakeRecords.Add(record);
}

internal sealed class SourcingGateRepository(EmployeeDbContext db) : ISourcingGateRepository
{
    public Task<SourcingGate?> GetAsync(string reqId, CancellationToken ct) =>
        db.SourcingGates.FirstOrDefaultAsync(g => g.ReqId == reqId, ct);

    public void Add(SourcingGate gate) => db.SourcingGates.Add(gate);
}
