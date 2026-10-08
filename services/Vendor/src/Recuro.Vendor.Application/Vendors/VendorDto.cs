using System.Text.Json.Serialization;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.Vendor.Domain.Vendors;
using VendorEntity = Recuro.Vendor.Domain.Vendors.Vendor;

namespace Recuro.Vendor.Application.Vendors;

public sealed record FeeTermsDto(
    string Kind,
    decimal Value,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Justification,
    string TermsVersion,
    bool WithinBand);

public sealed record GatesDto(bool Experience, bool TrackRecord, bool Agreement, bool Nda, bool ReplacementGuarantee, bool PrivacyAck);

public sealed record DocumentsDto(string? NdaRef, string? AgreementRef, string? PrivacyAckRef);

/// <summary>
/// A vendor on the S-14 list (FRD §9 <c>Vendor</c>: type, criteria[6], fee terms, status, documents).
/// The frontend has no Vendor mock yet (S-14 is a P1 placeholder), so this shape is the proposed contract.
/// </summary>
public sealed record VendorDto(
    string Id,
    string Name,
    string Type,
    FeeTermsDto FeeTerms,
    GatesDto Gates,
    int PendingGates,
    DocumentsDto Documents,
    string Status,
    DateTimeOffset? EmpanelledAt,
    string? ApprovedBy,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DateTimeOffset? DeEmpanelledAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DeEmpanelReason)
{
    public static VendorDto From(VendorEntity vendor, FeeBand band)
    {
        ArgumentNullException.ThrowIfNull(vendor);
        ArgumentNullException.ThrowIfNull(band);
        var g = vendor.Gates;
        var d = vendor.Documents;
        return new VendorDto(
            vendor.Id.ToString(),
            vendor.Name,
            vendor.Type.ToString(),
            new FeeTermsDto(vendor.Fee.Kind.ToString(), vendor.Fee.Value, vendor.Fee.Justification, vendor.Fee.TermsVersion, band.Contains(vendor.Fee)),
            new GatesDto(g.Experience, g.TrackRecord, g.Agreement, g.Nda, g.ReplacementGuarantee, g.PrivacyAck),
            g.Pending().Count,
            new DocumentsDto(d.NdaRef, d.AgreementRef, d.PrivacyAckRef),
            vendor.Status.ToString(),
            vendor.EmpanelledAt,
            vendor.ApprovedBy,
            vendor.DeEmpanelledAt,
            vendor.DeEmpanelReason);
    }
}

/// <summary>
/// <c>GET /api/v1/vendors/{vendorId}/status</c> (architecture.md, RCU-CND-005): <c>status</c> is
/// <c>active | pending | off</c>. <c>name</c> and <c>type</c> are additive, for Bgv's vendor picker.
/// </summary>
public sealed record VendorStatusDto(string VendorId, string Status, bool Active, string Name, string Type)
{
    public static VendorStatusDto From(VendorEntity vendor)
    {
        ArgumentNullException.ThrowIfNull(vendor);
        return new VendorStatusDto(vendor.Id.ToString(), vendor.Status.ToString().ToLowerInvariant(), vendor.IsActive, vendor.Name, vendor.Type.ToString());
    }
}

/// <summary>Field sizes shared by validators and the EF configuration.</summary>
public static class VendorLimits
{
    public const int NameLength = 200;
    public const int DocumentRefLength = 300;
    public const int JustificationLength = 2000;
    public const int ReasonLength = 2000;
    public const int TermsVersionLength = 40;
    public const int ActorLength = 200;
    public const int MinJustificationLength = 10;
}

internal static class Actors
{
    public static VendorActor From(ICurrentUser user) => new(user.UserId, user.Name ?? user.UserId ?? "unknown");
}
