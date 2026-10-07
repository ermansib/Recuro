using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.Requisition.Application.Abstractions;
using Recuro.Requisition.Domain.JobDescriptions;
using Recuro.Requisition.Domain.Requisitions;

namespace Recuro.Requisition.Infrastructure.Persistence;

/// <summary>The tenant query filter on <see cref="RequisitionDbContext"/> scopes every query.</summary>
internal sealed class RequisitionRepository(RequisitionDbContext db) : IRequisitionRepository
{
    public Task<ManpowerRequisition?> GetByReqIdAsync(string reqId, CancellationToken ct) =>
        db.Requisitions.FirstOrDefaultAsync(r => r.ReqId == reqId, ct);

    public Task<ManpowerRequisition?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.Requisitions.FirstOrDefaultAsync(r => r.Id == id, ct);

    public void Add(ManpowerRequisition requisition) => db.Requisitions.Add(requisition);

    public async Task<IReadOnlyList<ManpowerRequisition>> ListAsync(RequisitionFilter filter, CancellationToken ct)
    {
        var query = db.Requisitions.AsNoTracking();
        if (filter.State is { } state)
        {
            query = query.Where(r => r.State == state);
        }

        if (!string.IsNullOrWhiteSpace(filter.Grade))
        {
            query = query.Where(r => r.Details.Grade == filter.Grade);
        }

        if (!string.IsNullOrWhiteSpace(filter.Location))
        {
            query = query.Where(r => r.Details.Location == filter.Location);
        }

        if (!string.IsNullOrWhiteSpace(filter.Text))
        {
            var pattern = $"%{EscapeLike(filter.Text.Trim())}%";
            query = query.Where(r =>
                EF.Functions.ILike(r.ReqId, pattern, "\\")
                || EF.Functions.ILike(r.Details.Designation, pattern, "\\")
                || EF.Functions.ILike(r.Details.Department, pattern, "\\"));
        }

        return await query.OrderByDescending(r => r.CreatedAt).Take(filter.Limit).ToListAsync(ct);
    }

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);
}

internal sealed class JobDescriptionRepository(RequisitionDbContext db) : IJobDescriptionRepository
{
    public Task<JobDescription?> GetByReqIdAsync(string reqId, CancellationToken ct) =>
        db.JobDescriptions.FirstOrDefaultAsync(j => j.ReqId == reqId, ct);

    public Task<JobDescription?> GetByIdAsync(Guid id, CancellationToken ct) =>
        db.JobDescriptions.FirstOrDefaultAsync(j => j.Id == id, ct);

    public async Task<bool> ExistsForRequisitionAsync(Guid requisitionId, CancellationToken ct) =>
        db.JobDescriptions.Local.Any(j => j.RequisitionId == requisitionId)
        || await db.JobDescriptions.AnyAsync(j => j.RequisitionId == requisitionId, ct);

    public void Add(JobDescription jobDescription) => db.JobDescriptions.Add(jobDescription);
}

/// <summary>
/// One atomic upsert per number, outside any open transaction, so concurrent submits never get the same
/// REQ-ID and a failed submit never hands its number to someone else.
/// </summary>
internal sealed class ReqIdAllocator(RequisitionDbContext db, ITenantContext tenant) : IReqIdAllocator
{
    public async Task<string> NextAsync(int year, CancellationToken ct)
    {
        var tenantId = tenant.RequiredTenantId;
        var values = await db.Database.SqlQuery<int>($"""
            INSERT INTO req_id_sequences (tenant_id, year, last_value) VALUES ({tenantId}, {year}, 1)
            ON CONFLICT (tenant_id, year) DO UPDATE SET last_value = req_id_sequences.last_value + 1
            RETURNING last_value AS "Value"
            """).ToListAsync(ct);
        return string.Create(CultureInfo.InvariantCulture, $"REQ-{year}-{values.Single():0000}");
    }
}
