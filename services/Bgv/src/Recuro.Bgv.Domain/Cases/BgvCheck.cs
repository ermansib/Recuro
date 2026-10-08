using Recuro.BuildingBlocks.Domain;

namespace Recuro.Bgv.Domain.Cases;

/// <summary>
/// One verification check on a case (frontend <c>BgvCheck</c>). Moves only through
/// <see cref="CheckTransitions"/>, and only through its case.
/// </summary>
public sealed class BgvCheck
{
    private BgvCheck()
    {
    }

    /// <summary>Check key from the Config BGV matrix, e.g. <c>police</c>.</summary>
    public string Type { get; private set; } = string.Empty;

    public string Label { get; private set; } = string.Empty;

    public string Detail { get; private set; } = string.Empty;

    public CheckStatus Status { get; private set; }

    /// <summary>Progress or result note; for a not-applicable check, the reason.</summary>
    public string Note { get; private set; } = string.Empty;

    /// <summary>When the check reached its result.</summary>
    public DateOnly? Date { get; private set; }

    /// <summary>Salary or other sensitive detail, shown to HR roles only (masking map, RCU-BGV-008).</summary>
    public string? SensitiveNote { get; private set; }

    internal static BgvCheck Plan(PlannedCheck planned) => new()
    {
        Type = planned.Type,
        Label = planned.Label,
        Detail = planned.Detail,
        Status = planned.Applicable ? CheckStatus.Pending : CheckStatus.NotApplicable,
        Note = planned.Applicable ? string.Empty : planned.NotApplicableReason ?? string.Empty,
    };

    internal Result MoveTo(CheckStatus to, string? note, string? sensitiveNote, DateOnly? date)
    {
        var allowed = CheckTransitions.Table.EnsureCanMove(Status, to, $"Check {Type}");
        if (allowed.IsFailure)
        {
            return allowed;
        }

        Status = to;
        if (!string.IsNullOrWhiteSpace(note))
        {
            Note = note.Trim();
        }

        if (!string.IsNullOrWhiteSpace(sensitiveNote))
        {
            SensitiveNote = sensitiveNote.Trim();
        }

        if (to is CheckStatus.Cleared or CheckStatus.Flagged)
        {
            Date = date;
        }

        return Result.Success();
    }
}
