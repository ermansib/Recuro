using Microsoft.EntityFrameworkCore;
using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Domain.Tenants;

namespace Recuro.Admin.Infrastructure.Persistence.Seed;

/// <summary>Idempotent startup seeding: product catalogue always, demo tenants only when asked (development).</summary>
public sealed class DatabaseSeeder(AdminDbContext db, IClock clock)
{
    /// <summary>Fixed ids so the development sign-in can offer the demo tenants.</summary>
    public static readonly Guid AuroraTenantId = Guid.Parse("6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a01");
    public static readonly Guid TalentBridgeTenantId = Guid.Parse("6a1e3c4e-0b4d-4d55-9f5b-5f0d3b8a0a02");

    public async Task SeedAsync(bool includeDemoTenants, CancellationToken ct)
    {
        if (!await db.ThemePresets.AnyAsync(ct))
        {
            db.ThemePresets.AddRange(ThemePresetSeed.All());
        }

        if (!await db.ScreenDefinitions.AnyAsync(ct))
        {
            db.ScreenDefinitions.AddRange(ScreenCatalogSeed.All());
        }

        if (includeDemoTenants && !await db.Tenants.AnyAsync(ct))
        {
            db.Tenants.Add(Tenant.Create("Aurora Housing Finance", "aurora", TenantKind.InHouse, TenantPlan.Enterprise, clock.UtcNow, AuroraTenantId).Value);
            db.Tenants.Add(Tenant.Create("TalentBridge Staffing", "talentbridge", TenantKind.Agency, TenantPlan.Professional, clock.UtcNow, TalentBridgeTenantId).Value);
        }

        await db.SaveChangesAsync(ct);
    }
}
