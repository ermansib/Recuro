using Microsoft.EntityFrameworkCore;
using Recuro.Vendor.Application.Abstractions;
using Recuro.Vendor.Domain.Vendors;
using VendorEntity = Recuro.Vendor.Domain.Vendors.Vendor;

namespace Recuro.Vendor.Infrastructure.Persistence;

/// <summary>The tenant query filter on <see cref="VendorDbContext"/> scopes every query.</summary>
internal sealed class VendorRepository(VendorDbContext db) : IVendorRepository
{
    public Task<VendorEntity?> GetAsync(Guid id, CancellationToken ct) => db.Vendors.FirstOrDefaultAsync(v => v.Id == id, ct);

    public async Task<IReadOnlyList<VendorEntity>> ListAsync(VendorType? type, VendorStatus? status, CancellationToken ct)
    {
        var query = db.Vendors.AsNoTracking();
        if (type is { } t)
        {
            query = query.Where(v => v.Type == t);
        }

        if (status is { } s)
        {
            query = query.Where(v => v.Status == s);
        }

        return await query.OrderBy(v => v.Name).ToListAsync(ct);
    }

    public Task<bool> NameTakenAsync(string name, CancellationToken ct) =>
        db.Vendors.AnyAsync(v => EF.Functions.ILike(v.Name, LikeEscape(name), "\\"), ct);

    /// <summary>Case-insensitive equality through ILIKE: the name's own wildcards are matched literally.</summary>
    private static string LikeEscape(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal);

    public void Add(VendorEntity vendor) => db.Vendors.Add(vendor);
}
