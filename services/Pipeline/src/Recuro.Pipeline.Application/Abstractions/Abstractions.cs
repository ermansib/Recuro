using System.Text.Json;
using Recuro.Pipeline.Domain.Applications;
using Recuro.Pipeline.Domain.Requisitions;
using StageTatRule = Recuro.Pipeline.Application.Applications.StageTatRule;

namespace Recuro.Pipeline.Application.Abstractions;

/// <summary>Applications of the current tenant. Never returns another tenant's rows.</summary>
public interface IApplicationRepository
{
    /// <summary>Tracked, for changes.</summary>
    Task<Domain.Applications.Application?> GetAsync(string appId, CancellationToken ct);

    /// <summary>Read-only, oldest first.</summary>
    Task<IReadOnlyList<Domain.Applications.Application>> ListByRequisitionAsync(string reqId, CancellationToken ct);

    /// <summary>Tracked: the requisition's applications that can still move.</summary>
    Task<IReadOnlyList<Domain.Applications.Application>> ListOpenByRequisitionAsync(string reqId, CancellationToken ct);

    /// <summary>The candidate's application to this requisition that is still in progress, if any.</summary>
    Task<Domain.Applications.Application?> FindOpenAsync(string reqId, string candidateId, CancellationToken ct);

    /// <summary>Tracked: unflagged applications in <paramref name="stage"/> whose clock started before <paramref name="enteredBefore"/>.</summary>
    Task<IReadOnlyList<Domain.Applications.Application>> ListTatCandidatesAsync(
        ApplicationStage stage,
        DateTimeOffset enteredBefore,
        int limit,
        CancellationToken ct);

    /// <summary>How many of the tenant's applications sit in each stage (RCU-DSH-001 funnel).</summary>
    Task<IReadOnlyDictionary<ApplicationStage, int>> CountByStageAsync(CancellationToken ct);

    /// <summary>Read-only: applications whose current stage has overrun its TAT, longest-waiting first.</summary>
    Task<IReadOnlyList<Domain.Applications.Application>> ListTatBreachedAsync(int limit, CancellationToken ct);

    /// <summary>Applications in pre-boarding whose expected joining date falls between the two dates, inclusive.</summary>
    Task<int> CountJoiningBetweenAsync(DateOnly from, DateOnly to, CancellationToken ct);

    void Add(Domain.Applications.Application application);
}

public interface ISourcingGateRepository
{
    Task<SourcingGate?> GetAsync(string reqId, CancellationToken ct);

    void Add(SourcingGate gate);
}

/// <summary>Issues <c>APP-YYYY-####</c> ids, gap-free per tenant and year.</summary>
public interface IApplicationNumbers
{
    Task<string> NextAsync(int year, CancellationToken ct);
}

/// <summary>
/// Working-day arithmetic on the tenant's business calendar (Config <c>resolve/working-days</c>, BNFR-8).
/// The only place Pipeline counts working days.
/// </summary>
public interface IWorkingDayCalendar
{
    Task<DateOnly> AddAsync(DateOnly from, int days, CancellationToken ct);
}

/// <summary>Stage TATs in force for the tenant (Config TAT matrix, FRD §5.2), keyed by stage. Stages without a rule have no clock.</summary>
public interface ITatRules
{
    Task<IReadOnlyDictionary<ApplicationStage, StageTatRule>> GetAsync(CancellationToken ct);
}

/// <summary>
/// The Candidate service, called over HTTP with the caller's own token so its masking applies to the
/// caller's role. Pipeline stores no personal data.
/// </summary>
public interface ICandidateDirectory
{
    Task<bool> ExistsAsync(string candidateId, CancellationToken ct);

    /// <summary>Candidates in the frontend <c>Candidate</c> shape, keyed by id. Unknown ids are absent.</summary>
    Task<IReadOnlyDictionary<string, JsonElement>> GetManyAsync(IReadOnlyCollection<string> candidateIds, CancellationToken ct);
}
