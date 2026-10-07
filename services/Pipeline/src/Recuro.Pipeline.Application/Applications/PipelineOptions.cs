namespace Recuro.Pipeline.Application.Applications;

/// <summary>One stage's TAT standard (FRD §5.2) and who is escalated to on breach.</summary>
public sealed class StageTatRule
{
    public int WorkingDays { get; set; }

    public string Escalation { get; set; } = string.Empty;
}

/// <summary>
/// Pipeline rules bound from <c>Pipeline</c>. Defaults are FRD §5.2 / §9.6; per-tenant, versioned values
/// come from the Config service (RCU-CFG) when it is connected.
/// </summary>
public sealed class PipelineOptions
{
    public const string SectionName = "Pipeline";

    /// <summary>The regret email goes out within this many working days of final rejection.</summary>
    public int RegretWorkingDays { get; set; } = 3;

    /// <summary>Unsuccessful candidates' data is kept this long (FRD §5.7).</summary>
    public int RetentionDays { get; set; } = 365;

    /// <summary>Stage TATs keyed by stage name. Stages without a rule have no clock.</summary>
    public Dictionary<string, StageTatRule> StageTat { get; set; } = new(StringComparer.Ordinal)
    {
        ["Sourced"] = new() { WorkingDays = 7, Escalation = "hrhead" },
        ["Screened"] = new() { WorkingDays = 7, Escalation = "hrhead" },
        ["Interview"] = new() { WorkingDays = 5, Escalation = "hod" },
        ["BGV"] = new() { WorkingDays = 15, Escalation = "hrhead" },
        ["Offer"] = new() { WorkingDays = 2, Escalation = "hrhead" },
    };
}
