using Recuro.Vendor.Domain.Vendors;

namespace Recuro.Vendor.Application.Vendors;

/// <summary>
/// Bound from <c>Vendor</c>. The §15 fee band is policy: it lives here as configuration until Config
/// carries a vendor matrix, so a tenant with another band changes settings, not code.
/// </summary>
public sealed class VendorOptions
{
    public const string SectionName = "Vendor";

    public decimal FeeBandMinPercent { get; set; } = 5m;

    public decimal FeeBandMaxPercent { get; set; } = 8.33m;

    public FeeBand FeeBand => new(FeeBandMinPercent, FeeBandMaxPercent);
}
