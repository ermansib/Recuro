using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Domain;
using Recuro.Pipeline.Application.Abstractions;

namespace Recuro.Pipeline.Infrastructure.Persistence;

/// <summary>Last application number issued per tenant and year.</summary>
internal sealed class ApplicationCounter : ITenantOwned
{
    public Guid TenantId { get; private set; }

    public int Year { get; private set; }

    public int Last { get; private set; }
}

/// <summary>
/// <c>APP-YYYY-####</c> from an atomic upsert, so concurrent requests and replicas never get the same
/// number. The number is taken outside the business transaction: a failed save leaves a gap, never a clash.
/// </summary>
internal sealed class ApplicationNumbers(PipelineDbContext db, ITenantContext tenant) : IApplicationNumbers
{
    public async Task<string> NextAsync(int year, CancellationToken ct)
    {
        var tenantId = tenant.RequiredTenantId;
        var next = await db.Database.SqlQuery<int>($"""
            INSERT INTO application_counters (tenant_id, year, last) VALUES ({tenantId}, {year}, 1)
            ON CONFLICT (tenant_id, year) DO UPDATE SET last = application_counters.last + 1
            RETURNING last AS "Value"
            """).AsAsyncEnumerable().FirstAsync(ct);
        return $"APP-{year}-{next:D4}";
    }
}
