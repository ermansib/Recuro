using Recuro.BuildingBlocks.Domain;

namespace Recuro.Vendor.Domain.Vendors;

/// <summary>FRD §9 data model <c>Vendor.type</c>: a recruitment consultant or a BGV agency.</summary>
public enum VendorType
{
    Consultant,
    BgvAgency,
}

/// <summary>Empanelment state. <c>Pending</c> is a registered partner whose §15 gates are not all met yet.</summary>
public enum VendorStatus
{
    Pending,
    Active,
    Off,
}

/// <summary>How the vendor charges (§15): a percentage of annual CTC (consultants) or a fixed fee per case (BGV).</summary>
public enum FeeKind
{
    PercentOfCtc,
    FixedPerCase,
}

/// <summary>Legal moves as data. De-empanelled vendors stay off; re-empanelment is a new registration.</summary>
public static class VendorTransitions
{
    public static readonly TransitionTable<VendorStatus> Table = new(new Dictionary<VendorStatus, VendorStatus[]>
    {
        [VendorStatus.Pending] = [VendorStatus.Active],
        [VendorStatus.Active] = [VendorStatus.Off],
        [VendorStatus.Off] = [],
    });
}

/// <summary>
/// The six mandatory §15 empanelment criteria (RCU-VEN-001), as on the prototype's S-14 checklist.
/// </summary>
public sealed record EmpanelmentGates(
    bool Experience,
    bool TrackRecord,
    bool Agreement,
    bool Nda,
    bool ReplacementGuarantee,
    bool PrivacyAck)
{
    public static readonly EmpanelmentGates None = new(false, false, false, false, false, false);

    /// <summary>Gate names (camelCase, as in the API) that are still unchecked.</summary>
    public IReadOnlyList<string> Pending()
    {
        var pending = new List<string>();
        Add(Experience, "experience");
        Add(TrackRecord, "trackRecord");
        Add(Agreement, "agreement");
        Add(Nda, "nda");
        Add(ReplacementGuarantee, "replacementGuarantee");
        Add(PrivacyAck, "privacyAck");
        return pending;

        void Add(bool met, string name)
        {
            if (!met)
            {
                pending.Add(name);
            }
        }
    }
}

/// <summary>Document references kept on the vendor record (§15: NDA, agreement, privacy acknowledgement).</summary>
public sealed record VendorDocuments(string? NdaRef, string? AgreementRef, string? PrivacyAckRef)
{
    public static readonly VendorDocuments None = new(null, null, null);

    public IReadOnlyList<string> Missing()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(NdaRef))
        {
            missing.Add("ndaRef");
        }

        if (string.IsNullOrWhiteSpace(AgreementRef))
        {
            missing.Add("agreementRef");
        }

        if (string.IsNullOrWhiteSpace(PrivacyAckRef))
        {
            missing.Add("privacyAckRef");
        }

        return missing;
    }
}

/// <summary>Commercial terms (RCU-VND-002). <c>Justification</c> is mandatory when the fee is outside the policy band.</summary>
public sealed record FeeTerms(FeeKind Kind, decimal Value, string? Justification, string TermsVersion);

/// <summary>The policy fee band for percentage fees (§15: 5–8.33% of annual CTC). Configuration, not code.</summary>
public sealed record FeeBand(decimal MinPercent, decimal MaxPercent)
{
    public bool Contains(FeeTerms fee)
    {
        ArgumentNullException.ThrowIfNull(fee);
        return fee.Kind != FeeKind.PercentOfCtc || (fee.Value >= MinPercent && fee.Value <= MaxPercent);
    }
}
