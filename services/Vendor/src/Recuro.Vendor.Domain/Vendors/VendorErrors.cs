using System.Globalization;
using Recuro.BuildingBlocks.Domain;

namespace Recuro.Vendor.Domain.Vendors;

public static class VendorErrors
{
    public const int GateCount = 6;

    public static Error NotFound(string vendorId) => Error.NotFound("vendor_not_found", $"Vendor {vendorId} was not found.");

    public static Error NotPending(VendorStatus status) =>
        Error.Conflict("vendor_not_pending", $"The vendor is {status}; empanelment evidence can change only while it is pending.");

    public static readonly Error ReasonRequired = Error.Validation(
        [new FieldError("reason", "reason_required", "A de-empanelment reason is mandatory.")]);

    /// <summary>VND-001: "{pendingGates:n}" — each unchecked gate and missing document is a field error.</summary>
    public static Error GatesPending(IReadOnlyList<string> gates, IReadOnlyList<string> documents)
    {
        ArgumentNullException.ThrowIfNull(gates);
        ArgumentNullException.ThrowIfNull(documents);
        var fields = gates.Select(g => new FieldError($"gates.{g}", "gate_pending", $"§15 gate not met: {g}."))
            .Concat(documents.Select(d => new FieldError($"documents.{d}", "document_required", $"Document reference required: {d}.")))
            .ToList();
        var message = string.Create(CultureInfo.InvariantCulture, $"Empanelment blocked — {gates.Count} of {GateCount} §15 gates pending")
            + (documents.Count > 0 ? string.Create(CultureInfo.InvariantCulture, $", {documents.Count} document(s) missing.") : ".");
        return new Error("empanelment_gates_pending", message, ErrorType.Validation) { Fields = fields };
    }

    public static Error JustificationRequired(FeeBand band)
    {
        ArgumentNullException.ThrowIfNull(band);
        return Error.Validation(
        [
            new FieldError(
                "fee.justification",
                "justification_required",
                string.Create(CultureInfo.InvariantCulture, $"The fee is outside the {band.MinPercent}–{band.MaxPercent}% band; a justification is mandatory.")),
        ]);
    }
}
