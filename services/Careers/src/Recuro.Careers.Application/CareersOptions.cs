namespace Recuro.Careers.Application;

/// <summary>Bound from <c>Careers</c>.</summary>
public sealed class CareersOptions
{
    public const string SectionName = "Careers";

    /// <summary>
    /// FRD §9.2.1: working days a new requisition is advertised internally (IJP) before the public site
    /// shows it. 0 turns the window off. Moves to the Config service's rules when it carries the IJP policy.
    /// </summary>
    public int IjpWindowWorkingDays { get; set; } = 5;

    /// <summary>Wording version of the privacy notice and COI declaration on the apply form.</summary>
    public string ConsentTextVersion { get; set; } = "v1";

    /// <summary>RCU-CAR-001: how long a public search page is cached.</summary>
    public TimeSpan SearchCacheFor { get; set; } = TimeSpan.FromSeconds(60);

    public int DefaultPageSize { get; set; } = 20;

    public int MaxPageSize { get; set; } = 50;
}
