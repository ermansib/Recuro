using Microsoft.Extensions.Logging;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Domain;
using Recuro.Employee.Application.Abstractions;
using Recuro.Employee.Domain.Intake;

namespace Recuro.Employee.Application;

/// <summary>
/// The internal candidate intake saga (RCU-BKD-001 §6.2), shared by IJP applications and referrals. The
/// record is saved first; then the Candidate service records the person (an existing record is reused);
/// then Pipeline creates the application. If that last step fails, a candidate this saga created is
/// tombstoned. Each step is saved, so the record always says how far the saga got.
/// </summary>
internal sealed partial class IntakeSaga(
    ICandidateIntake candidates,
    IPipelineIntake pipeline,
    IUnitOfWork unitOfWork,
    TimeProvider clock,
    ILogger<IntakeSaga> logger)
{
    /// <summary>
    /// Runs the saga for a record that was added to the unit of work but not saved yet.
    /// <paramref name="onCandidate"/> sees the Candidate service's answer before the application step.
    /// </summary>
    public async Task<Result<string>> RunAsync(
        IntakeRecord record,
        NewCandidate candidate,
        string note,
        Action<CandidateRef>? onCandidate,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(record);
        await unitOfWork.SaveChangesAsync(ct);

        IntakeOutcome<CandidateRef> person;
        try
        {
            person = await candidates.CreateAsync(candidate, ct);
        }
        catch (DependencyUnavailableException)
        {
            record.Fail("dependency_unavailable", compensated: null, clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(CancellationToken.None);
            throw;
        }

        if (person.Error is { } candidateError)
        {
            record.Fail(candidateError.Code, compensated: null, clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(ct);
            return candidateError;
        }

        var recorded = person.Value!;
        record.CandidateRecorded(recorded.CandidateId, recorded.CreatedHere, clock.GetUtcNow());
        onCandidate?.Invoke(recorded);
        await unitOfWork.SaveChangesAsync(ct);

        IntakeOutcome<string> application;
        try
        {
            application = await pipeline.CreateApplicationAsync(record.ReqId, recorded.CandidateId, candidate.Source, note, ct);
        }
        catch (DependencyUnavailableException)
        {
            await CompensateAsync(record, "dependency_unavailable", ct);
            throw;
        }

        if (application.Error is { } applicationError)
        {
            await CompensateAsync(record, applicationError.Code, ct);
            return applicationError;
        }

        record.Complete(application.Value!, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(ct);
        return application.Value!;
    }

    private async Task CompensateAsync(IntakeRecord record, string code, CancellationToken ct)
    {
        bool? compensated = null;
        if (record.CandidateCreatedHere && record.CandidateId is { } candidateId)
        {
            try
            {
                compensated = await candidates.TombstoneAsync(candidateId, $"Employee intake failed: {code}", ct);
            }
            catch (DependencyUnavailableException ex)
            {
                CompensationFailed(logger, record.Id, ex);
                compensated = false;
            }
        }

        record.Fail(code, compensated, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Intake {RecordId}: tombstoning the candidate failed; retention will purge it")]
    private static partial void CompensationFailed(ILogger logger, Guid recordId, Exception ex);
}

/// <summary>Consent type names, the Candidate service's <c>ConsentType</c> spelling.</summary>
public static class ConsentTypes
{
    public const string DataPrivacy = "DataPrivacy";
    public const string ConflictOfInterest = "ConflictOfInterest";
}

/// <summary>Field sizes shared by validators and the EF configuration.</summary>
public static class EmployeeLimits
{
    public const int ReqIdLength = 40;
    public const int AppIdLength = 40;
    public const int CandidateIdLength = 64;
    public const int UserIdLength = 200;
    public const int NameLength = 200;
    public const int EmailLength = 254;
    public const int PhoneLength = 32;
    public const int TitleLength = 200;
    public const int ShortTextLength = 120;
    public const int GradeLength = 20;
    public const int MaxGrades = 20;
    public const int SummaryLength = 2000;
    public const int CodeLength = 60;
    public const int FileNameLength = 255;
}
