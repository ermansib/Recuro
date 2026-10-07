using Recuro.BuildingBlocks.Application.Messaging;

namespace Recuro.Candidate.Application.Candidates.Commands.CreateCandidate;

/// <summary>A consent as captured by the intake form.</summary>
public sealed record ConsentInput(string Type, string TextVersion, DateTimeOffset? At);

/// <summary>
/// RCU-CND-001/005: creates a candidate with consents and source attribution. Intake flows (HR-TA's
/// "log candidate", the careers site, IJP and referrals) call this first, then create the application
/// in the Pipeline service.
/// </summary>
public sealed record CreateCandidateCommand(
    string Name,
    string Email,
    string? Phone,
    decimal ExperienceYears,
    string? Summary,
    decimal? CurrentCtc,
    decimal? ExpectedCtc,
    int? NoticeDays,
    string Source,
    string? SourceRef,
    string? ReferrerId,
    string? ConsultantId,
    string? Channel,
    IReadOnlyList<ConsentInput> Consents,
    string ConsentSource) : ICommand<CandidateDto>;
