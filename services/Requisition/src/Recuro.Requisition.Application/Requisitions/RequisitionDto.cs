using System.Globalization;
using Recuro.Requisition.Domain.Requisitions;

namespace Recuro.Requisition.Application.Requisitions;

/// <summary>The frontend's <c>Requisition</c> (frontend/src/domain/types.ts), field for field.</summary>
public sealed record RequisitionDto(
    string ReqId,
    string Department,
    string Designation,
    string Grade,
    string Location,
    int Positions,
    string ReportingManager,
    EmploymentType EmploymentType,
    RequisitionNature Nature,
    string ReplacementReason,
    string JoiningDate,
    string Band,
    bool OutOfBudget,
    string OobJustification,
    string Qualifications,
    IReadOnlyList<string> SourcingChannels,
    RequisitionState State,
    string Owner,
    string RaisedAt,
    string TargetClosure,
    RouteDto Route,
    int AgeDays)
{
    public static RequisitionDto From(ManpowerRequisition requisition, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(requisition);
        var details = requisition.Details;
        var raisedAt = DateOnly.FromDateTime((requisition.SubmittedAt ?? requisition.CreatedAt).UtcDateTime);
        return new RequisitionDto(
            requisition.ReqId,
            details.Department,
            details.Designation,
            details.Grade,
            details.Location,
            details.Positions,
            details.ReportingManager,
            details.EmploymentType,
            details.Nature,
            details.ReplacementReason,
            IsoDate(details.JoiningDate),
            details.Band,
            details.OutOfBudget,
            details.OobJustification,
            details.Qualifications,
            details.SourcingChannels,
            requisition.State,
            requisition.OwnerName,
            IsoDate(raisedAt),
            IsoDate(requisition.TargetClosure),
            new RouteDto(requisition.Route.Initiating, requisition.Route.Recommending, requisition.Route.Approving, requisition.Route.ApproverRole),
            Math.Max(0, today.DayNumber - raisedAt.DayNumber));
    }

    public static string IsoDate(DateOnly? date) =>
        date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;
}

/// <summary>Frontend <c>Pick&lt;DoaRoute, 'initiating' | 'recommending' | 'approving' | 'approverRole'&gt;</c>.</summary>
public sealed record RouteDto(string Initiating, string Recommending, string Approving, string ApproverRole);

/// <summary>A requisition with its concurrency version, which the API returns as the ETag.</summary>
public sealed record VersionedRequisition(RequisitionDto Requisition, uint Version);

/// <summary>
/// The frontend's <c>RequisitionInput</c>: what the MRF wizard sends. Dates are ISO strings and may be
/// empty while the requisition is a draft.
/// </summary>
public sealed record RequisitionInput(
    string? Department,
    string? Designation,
    string? Grade,
    string? Location,
    int Positions,
    string? ReportingManager,
    EmploymentType EmploymentType,
    RequisitionNature Nature,
    string? ReplacementReason,
    string? JoiningDate,
    string? Band,
    bool OutOfBudget,
    string? OobJustification,
    string? Qualifications,
    IReadOnlyList<string>? SourcingChannels)
{
    public RequisitionDetails ToDetails() => new(
        Clean(Department),
        Clean(Designation),
        Clean(Grade),
        Clean(Location),
        Positions,
        Clean(ReportingManager),
        EmploymentType,
        Nature,
        Clean(ReplacementReason),
        ParseDate(JoiningDate),
        Clean(Band),
        OutOfBudget,
        Clean(OobJustification),
        Clean(Qualifications),
        (SourcingChannels ?? []).Select(Clean).Where(c => c.Length > 0).Distinct(StringComparer.Ordinal).ToList());

    public static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;
}
