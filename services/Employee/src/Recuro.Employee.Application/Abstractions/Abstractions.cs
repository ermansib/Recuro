using Recuro.BuildingBlocks.Domain;
using Recuro.Employee.Domain.Ijp;
using Recuro.Employee.Domain.Referrals;
using Recuro.Employee.Domain.Requisitions;

namespace Recuro.Employee.Application.Abstractions;

public interface ISourcingGateRepository
{
    Task<SourcingGate?> GetAsync(string reqId, CancellationToken ct);

    void Add(SourcingGate gate);
}

/// <summary>Internal postings of the current tenant.</summary>
public interface IIjpPostingRepository
{
    /// <summary>Tracked, for changes.</summary>
    Task<IjpPosting?> GetAsync(string reqId, CancellationToken ct);

    /// <summary>Read-only: postings listed at <paramref name="now"/>, closing soonest first.</summary>
    Task<IReadOnlyList<IjpPosting>> ListOpenAsync(DateTimeOffset now, CancellationToken ct);

    void Add(IjpPosting posting);
}

/// <summary>IJP applications and referrals of the current tenant.</summary>
public interface IIntakeRepository
{
    /// <summary>Read-only: the employee's own IJP applications, newest first (RCU-EMP-005).</summary>
    Task<IReadOnlyList<InternalApplication>> ListApplicationsAsync(string employeeId, CancellationToken ct);

    /// <summary>Read-only: the employee's own referrals, newest first (RCU-EMP-005).</summary>
    Task<IReadOnlyList<Referral>> ListReferralsAsync(string employeeId, CancellationToken ct);

    /// <summary>Read-only: the employee's IJP applications to one requisition.</summary>
    Task<IReadOnlyList<InternalApplication>> ListApplicationsAsync(string employeeId, string reqId, CancellationToken ct);

    /// <summary>Tracked: an IJP application or referral by the APP-ID Pipeline issued.</summary>
    Task<Domain.Intake.IntakeRecord?> GetByAppIdAsync(string appId, CancellationToken ct);

    void Add(Domain.Intake.IntakeRecord record);
}

/// <summary>The person behind an application, as the Candidate service recorded them.</summary>
/// <param name="CandidateId">The Candidate service's id.</param>
/// <param name="CreatedHere">False when the person matched an existing candidate (RCU-CND-002).</param>
public sealed record CandidateRef(string CandidateId, bool CreatedHere);

/// <summary>A consent with the exact wording version.</summary>
public sealed record ConsentGiven(string Type, string TextVersion, DateTimeOffset At);

/// <summary>What an intake sends to the Candidate service.</summary>
public sealed record NewCandidate(
    string Name,
    string Email,
    string? Phone,
    decimal ExperienceYears,
    string Source,
    string? ReferrerId,
    string Channel,
    IReadOnlyList<ConsentGiven> Consents,
    string ConsentSource);

/// <summary>The Candidate service (<c>POST /api/v1/candidates</c>), called as this service. A duplicate resolves to the existing record.</summary>
public interface ICandidateIntake
{
    Task<IntakeOutcome<CandidateRef>> CreateAsync(NewCandidate candidate, CancellationToken ct);

    /// <summary>Saga compensation: removes a candidate this saga created. False when it could not be done.</summary>
    Task<bool> TombstoneAsync(string candidateId, string reason, CancellationToken ct);
}

/// <summary>The Pipeline service (<c>POST /api/v1/pipeline/applications</c>), called as this service.</summary>
public interface IPipelineIntake
{
    Task<IntakeOutcome<string>> CreateApplicationAsync(string reqId, string candidateId, string source, string note, CancellationToken ct);
}

/// <summary>A call to another service: its value, or the 4xx error it answered with. Outages throw <see cref="DependencyUnavailableException"/>.</summary>
public sealed record IntakeOutcome<T>(T? Value, Error? Error);

/// <summary>Factories for <see cref="IntakeOutcome{T}"/>.</summary>
public static class IntakeOutcome
{
    public static IntakeOutcome<T> Ok<T>(T value) => new(value, null);

    public static IntakeOutcome<T> Refused<T>(Error error) => new(default, error);
}

/// <summary>Working days on the tenant's business calendar (Config <c>resolve/working-days</c>, BNFR-8).</summary>
public interface IWorkingDayCalendar
{
    Task<DateOnly> AddAsync(DateOnly from, int days, CancellationToken ct);
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
