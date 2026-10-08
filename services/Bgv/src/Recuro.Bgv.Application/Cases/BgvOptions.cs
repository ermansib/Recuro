namespace Recuro.Bgv.Application.Cases;

/// <summary>Bound from <c>Bgv</c>. Rules live in Config; these are service mechanics only.</summary>
public sealed class BgvOptions
{
    public const string SectionName = "Bgv";

    /// <summary>Working days each adverse-escalation leg has before Workflow escalates it.</summary>
    public int AdverseLegSlaWorkingDays { get; set; } = 2;

    /// <summary>The dashboard's "due soon" window (mock: "1 due within 48h").</summary>
    public TimeSpan DueSoonWindow { get; set; } = TimeSpan.FromHours(48);
}
