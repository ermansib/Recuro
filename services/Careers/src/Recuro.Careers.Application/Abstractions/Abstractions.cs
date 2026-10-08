using Recuro.Careers.Domain.Applications;
using Recuro.Careers.Domain.Postings;
using Recuro.Careers.Domain.Requisitions;

namespace Recuro.Careers.Application.Abstractions;

/// <summary>Postings of the current tenant. Never returns another tenant's rows.</summary>
public interface IPostingRepository
{
    /// <summary>Tracked, for changes.</summary>
    Task<JobPosting?> GetByReqIdAsync(string reqId, CancellationToken ct);

    /// <summary>Read-only.</summary>
    Task<JobPosting?> GetByPostingIdAsync(string postingId, CancellationToken ct);

    /// <summary>Read-only, newest change first (HR view).</summary>
    Task<IReadOnlyList<JobPosting>> ListAsync(CancellationToken ct);

    /// <summary>
    /// RCU-CAR-001: postings the public can see at <paramref name="now"/>, filtered and ordered by
    /// (visibleFrom desc, postingId), starting after <see cref="JobSearch.After"/>.
    /// </summary>
    Task<IReadOnlyList<JobPosting>> SearchVisibleAsync(JobSearch search, DateTimeOffset now, CancellationToken ct);

    void Add(JobPosting posting);
}

/// <summary>Filters of the public search. <see cref="After"/> is the keyset cursor (last item of the previous page).</summary>
public sealed record JobSearch(string? Query, string? Location, string? Industry, SearchCursor? After, int Limit);

/// <summary>Keyset position: the last posting of the previous page.</summary>
public sealed record SearchCursor(DateTimeOffset VisibleFrom, string PostingId);

public interface ISourcingGateRepository
{
    Task<SourcingGate?> GetAsync(string reqId, CancellationToken ct);

    void Add(SourcingGate gate);
}

public interface IPublicApplicationRepository
{
    /// <summary>Tracked; by the APP-ID Pipeline issued.</summary>
    Task<PublicApplication?> GetByAppIdAsync(string appId, CancellationToken ct);

    /// <summary>Read-only; a finished or running intake for the same Idempotency-Key and email.</summary>
    Task<PublicApplication?> FindByClientKeyAsync(string clientKey, CancellationToken ct);

    /// <summary>Tracked; the application whose final rejection had this event id (regret receipt).</summary>
    Task<PublicApplication?> GetByFinalRejectedEventAsync(Guid eventId, CancellationToken ct);

    void Add(PublicApplication application);
}

/// <summary>The person behind an application, as the Candidate service recorded them.</summary>
/// <param name="CandidateId">The Candidate service's id.</param>
/// <param name="CreatedHere">False when the applicant matched an existing candidate (RCU-CND-002).</param>
public sealed record CandidateRef(string CandidateId, bool CreatedHere);

/// <summary>What the intake saga sends to the Candidate service. Consents carry their wording version.</summary>
public sealed record NewCandidate(
    string Name,
    string Email,
    string Phone,
    decimal ExperienceYears,
    decimal? CurrentCtc,
    decimal? ExpectedCtc,
    int? NoticeDays,
    string Channel,
    IReadOnlyList<GivenConsent> Consents);

/// <summary>
/// The Candidate service (<c>POST /api/v1/candidates</c>), called as this service. A duplicate
/// (409 <c>duplicate_candidate</c>) resolves to the existing record, as HR-TA's log-candidate flow does.
/// </summary>
public interface ICandidateIntake
{
    /// <summary>A rejected input comes back as the Candidate service's validation error.</summary>
    Task<IntakeOutcome<CandidateRef>> CreateAsync(NewCandidate candidate, CancellationToken ct);

    /// <summary>Saga compensation: removes a candidate this saga created. False when it could not be done.</summary>
    Task<bool> TombstoneAsync(string candidateId, string reason, CancellationToken ct);
}

/// <summary>The Pipeline service (<c>POST /api/v1/pipeline/applications</c>), called as this service.</summary>
public interface IPipelineIntake
{
    /// <summary>The APP-ID on success; 409s (sourcing locked, already applied) come back as conflicts.</summary>
    Task<IntakeOutcome<string>> CreateApplicationAsync(string reqId, string candidateId, string source, string note, CancellationToken ct);
}

/// <summary>A call to another service: its value, or the error it answered with (4xx). Outages throw <see cref="DependencyUnavailableException"/>.</summary>
public sealed record IntakeOutcome<T>(T? Value, BuildingBlocks.Domain.Error? Error);

/// <summary>Factories for <see cref="IntakeOutcome{T}"/>.</summary>
public static class IntakeOutcome
{
    public static IntakeOutcome<T> Ok<T>(T value) => new(value, null);

    public static IntakeOutcome<T> Refused<T>(BuildingBlocks.Domain.Error error) => new(default, error);
}

/// <summary>Working days on the tenant's business calendar (Config <c>resolve/working-days</c>, BNFR-8).</summary>
public interface IWorkingDayCalendar
{
    Task<DateOnly> AddAsync(DateOnly from, int days, CancellationToken ct);
}

/// <summary>Short-lived cache of public search pages (RCU-CAR-001: 60 s). Keys always include the tenant.</summary>
public interface IJobSearchCache
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct)
        where T : class;

    Task SetAsync<T>(string key, T value, CancellationToken ct)
        where T : class;
}

/// <summary>A service this request depends on did not answer. The API maps it to 503.</summary>
public sealed class DependencyUnavailableException : Exception
{
    public DependencyUnavailableException()
    {
    }

    public DependencyUnavailableException(string message)
        : base(message)
    {
    }

    public DependencyUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
