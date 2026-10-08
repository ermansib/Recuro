namespace Recuro.Requisition.Domain.Requisitions;

/// <summary>
/// Annexure-A fields an HR-TA fills in the MRF wizard (the frontend's <c>RequisitionInput</c>).
/// A draft may be incomplete; <see cref="MissingForSubmit"/> lists what submit still needs.
/// </summary>
public sealed record RequisitionDetails(
    string Department,
    string Designation,
    string Grade,
    string Location,
    int Positions,
    string ReportingManager,
    EmploymentType EmploymentType,
    RequisitionNature Nature,
    string ReplacementReason,
    DateOnly? JoiningDate,
    string Band,
    bool OutOfBudget,
    string OobJustification,
    string Qualifications,
    IReadOnlyList<string> SourcingChannels)
{
    /// <summary>RCU-REQ-001/003: field names (camelCase, as on the wire) that block submit, with a code and message.</summary>
    public IReadOnlyList<(string Field, string Code, string Message)> MissingForSubmit()
    {
        var missing = new List<(string, string, string)>();
        Require(missing, "department", Department);
        Require(missing, "designation", Designation);
        Require(missing, "grade", Grade);
        Require(missing, "location", Location);
        if (Positions < 1)
        {
            missing.Add(("positions", "min", "At least one position is required."));
        }

        Require(missing, "reportingManager", ReportingManager);
        if (Nature != RequisitionNature.NewPosition)
        {
            Require(missing, "replacementReason", ReplacementReason, "Replacement or backfill reason is required.");
        }

        if (JoiningDate is null)
        {
            missing.Add(("joiningDate", "required", "joiningDate is required."));
        }

        Require(missing, "band", Band);
        Require(missing, "qualifications", Qualifications);
        if (OutOfBudget && OobJustification.Trim().Length < RequisitionLimits.MinOobJustificationLength)
        {
            missing.Add((
                "oobJustification",
                "oob_justification_required",
                $"Out-of-budget requisitions need a justification of at least {RequisitionLimits.MinOobJustificationLength} characters."));
        }

        return missing;
    }

    private static void Require(List<(string, string, string)> missing, string field, string value, string? message = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            missing.Add((field, "required", message ?? $"{field} is required."));
        }
    }
}

/// <summary>Column sizes and rule constants shared by validators and the EF configuration.</summary>
public static class RequisitionLimits
{
    public const int ShortText = 200;
    public const int LongText = 2000;
    public const int MaxPositions = 500;
    public const int MaxSourcingChannels = 20;
    public const int MinOobJustificationLength = 10;
    public const int MinReasonLength = 10;
    public const int ReqIdLength = 32;
    public const int ConfigVersionLength = 100;
}
