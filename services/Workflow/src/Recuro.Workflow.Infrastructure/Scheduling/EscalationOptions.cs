namespace Recuro.Workflow.Infrastructure.Scheduling;

/// <summary>Settings for the escalation scheduler (section <c>Escalation</c>).</summary>
public sealed class EscalationOptions
{
    public const string SectionName = "Escalation";

    /// <summary>Turn off in tests that drive <see cref="EscalationScheduler.RunOnceAsync"/> themselves.</summary>
    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Instances handled per run.</summary>
    public int BatchSize { get; set; } = 100;
}
