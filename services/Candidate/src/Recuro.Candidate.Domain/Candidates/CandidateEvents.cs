using Recuro.BuildingBlocks.Domain;

namespace Recuro.Candidate.Domain.Candidates;

public sealed record CandidateCreatedDomainEvent(Guid CandidateId, CandidateSource Source) : IDomainEvent;

/// <summary>Personal data was scrubbed. <see cref="ResumeStorageKey"/> is the CV file to delete, if there was one.</summary>
public sealed record CandidatePurgedDomainEvent(Guid CandidateId, string? ResumeStorageKey) : IDomainEvent;
