using Recuro.Vendor.Domain.Vendors;
using VendorEntity = Recuro.Vendor.Domain.Vendors.Vendor;

namespace Recuro.Vendor.Application.Abstractions;

/// <summary>Vendors of the current tenant. Never returns another tenant's rows.</summary>
public interface IVendorRepository
{
    /// <summary>Tracked, for changes.</summary>
    Task<VendorEntity?> GetAsync(Guid id, CancellationToken ct);

    /// <summary>Read-only, by name.</summary>
    Task<IReadOnlyList<VendorEntity>> ListAsync(VendorType? type, VendorStatus? status, CancellationToken ct);

    Task<bool> NameTakenAsync(string name, CancellationToken ct);

    void Add(VendorEntity vendor);
}
