namespace Recuro.Candidate.Application.Candidates;

/// <summary>Input limits, shared by validators and the EF configuration.</summary>
public static class CandidateLimits
{
    public const int NameLength = 200;
    public const int EmailLength = 254;
    public const int PhoneLength = 32;
    public const int SummaryLength = 500;
    public const int RefLength = 100;
    public const int TextVersionLength = 20;
    public const int ConsentSourceLength = 50;
    public const int ReasonLength = 1000;
    public const int MaxBatch = 200;
    public const decimal MaxExperienceYears = 60;
    public const decimal MaxCtc = 100_000;
    public const int MaxNoticeDays = 365;
    public const long MaxResumeBytes = 5 * 1024 * 1024;
    public const int FileNameLength = 200;

    public static readonly IReadOnlySet<string> ResumeContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "application/pdf",
        "application/msword",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
    };
}
