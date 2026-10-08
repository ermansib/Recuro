using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Careers.Application.Abstractions;
using Recuro.Careers.Application.Postings;
using Recuro.Careers.Domain.Applications;

namespace Recuro.Careers.Application.Applications;

/// <summary>The frontend's <c>PublicApplicationInput</c> plus the client's Idempotency-Key, if it sent one.</summary>
public sealed record SubmitPublicApplicationCommand(
    string PostingId,
    string Name,
    string Email,
    string Phone,
    decimal? ExperienceYears,
    decimal? CurrentCtc,
    decimal? ExpectedCtc,
    int? NoticeDays,
    string ResumeFileName,
    bool PrivacyConsent,
    bool CoiDeclaration,
    string? IdempotencyKey) : ICommand<PublicApplicationResultDto>;

/// <summary>
/// RCU-CAR-002: both consents are mandatory and enforced here, not only in the form. Errors name the
/// FRD section the consent comes from. 400 with field errors, like every other validation failure.
/// </summary>
internal sealed class SubmitPublicApplicationCommandValidator : AbstractValidator<SubmitPublicApplicationCommand>
{
    private static readonly string[] ResumeExtensions = [".pdf", ".doc", ".docx"];

    public SubmitPublicApplicationCommandValidator()
    {
        RuleFor(c => c.PrivacyConsent).Equal(true).WithErrorCode("consent_required")
            .WithMessage("Data-privacy consent is mandatory (FRD §14).");
        RuleFor(c => c.CoiDeclaration).Equal(true).WithErrorCode("consent_required")
            .WithMessage("The conflict-of-interest declaration is mandatory (FRD §7.1).");
        RuleFor(c => c.PostingId).NotEmpty().WithMessage("Select a position to apply for.").MaximumLength(CareersLimits.PostingIdLength);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(CareersLimits.NameLength);
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(CareersLimits.EmailLength);
        RuleFor(c => c.Phone).NotEmpty().MaximumLength(CareersLimits.PhoneLength)
            .Matches(@"^[+0-9 ()\-]*$").WithMessage("Mobile may contain digits, spaces, +, - and brackets only.");
        RuleFor(c => c.ExperienceYears).InclusiveBetween(0, 60);
        RuleFor(c => c.CurrentCtc).GreaterThanOrEqualTo(0);
        RuleFor(c => c.ExpectedCtc).GreaterThanOrEqualTo(0);
        RuleFor(c => c.NoticeDays).InclusiveBetween(0, 365);
        RuleFor(c => c.ResumeFileName).NotEmpty().WithMessage("Please attach your resume.")
            .MaximumLength(CareersLimits.FileNameLength)
            .Must(name => ResumeExtensions.Any(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
            .WithMessage("The resume must be a PDF or Word file.");
    }
}

/// <summary>
/// The frontend's <c>PublicApplicationResult</c>. <c>duplicateOf</c> is never filled on the public
/// site: telling an anonymous caller that an email is already on file would leak personal data.
/// </summary>
public sealed record PublicApplicationResultDto(string AppId, string Position);

/// <summary>
/// RCU-CAR-002/004: the candidate intake saga (RCU-BKD-001 §6.2), owned here. Consents are stored first,
/// then the Candidate service records the person (an existing record is reused), then Pipeline creates the
/// application and issues the APP-ID. If the application step fails, a candidate this saga created is
/// tombstoned. Success publishes <c>career.job.applied.v1</c>; Notification sends the confirmation and
/// HR-TA's alert.
/// </summary>
internal sealed partial class SubmitPublicApplicationCommandHandler(
    IPostingRepository postings,
    IPublicApplicationRepository applications,
    ICandidateIntake candidates,
    IPipelineIntake pipeline,
    IUnitOfWork unitOfWork,
    IOptions<CareersOptions> options,
    TimeProvider clock,
    ILogger<SubmitPublicApplicationCommandHandler> logger) : ICommandHandler<SubmitPublicApplicationCommand, PublicApplicationResultDto>
{
    private const string Source = "Portal";
    private const string Channel = "careers-site";

    public async Task<Result<PublicApplicationResultDto>> Handle(SubmitPublicApplicationCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var posting = await postings.GetByPostingIdAsync(command.PostingId, ct);
        if (posting is null || !posting.IsVisibleAt(now))
        {
            return ApplicationErrors.PostingUnavailable;
        }

        var clientKey = ClientKey(command.IdempotencyKey, command.Email);
        if (clientKey is not null && await applications.FindByClientKeyAsync(clientKey, ct) is { } earlier)
        {
            return earlier is { State: IntakeState.Completed, AppId: { } appId }
                ? new PublicApplicationResultDto(appId, earlier.Position)
                : ApplicationErrors.AlreadySubmitted;
        }

        var version = options.Value.ConsentTextVersion;
        var consents = new[] { new GivenConsent(ConsentTypes.DataPrivacy, version, now), new GivenConsent(ConsentTypes.ConflictOfInterest, version, now) };
        var intake = PublicApplication.Start(posting.PostingId, posting.ReqId, posting.Title, clientKey, consents, now);
        applications.Add(intake);
        await unitOfWork.SaveChangesAsync(ct);

        try
        {
            return await RunSagaAsync(intake, command, consents, ct);
        }
        catch (DependencyUnavailableException)
        {
            await FailAsync(intake, "dependency_unavailable", ct);
            throw;
        }
    }

    private async Task<Result<PublicApplicationResultDto>> RunSagaAsync(
        PublicApplication intake,
        SubmitPublicApplicationCommand command,
        IReadOnlyList<GivenConsent> consents,
        CancellationToken ct)
    {
        var candidate = await candidates.CreateAsync(
            new NewCandidate(
                command.Name.Trim(),
                command.Email.Trim(),
                command.Phone.Trim(),
                command.ExperienceYears ?? 0,
                command.CurrentCtc,
                command.ExpectedCtc,
                command.NoticeDays,
                Channel,
                consents),
            ct);
        if (candidate.Error is { } candidateError)
        {
            intake.Fail(candidateError.Code, compensated: null, clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(ct);
            return candidateError;
        }

        var person = candidate.Value!;
        intake.CandidateRecorded(person.CandidateId, person.CreatedHere, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(ct);

        var note = person.CreatedHere
            ? "Applied via career portal · consents captured"
            : "Applied via career portal · possible duplicate — review";
        IntakeOutcome<string> application;
        try
        {
            application = await pipeline.CreateApplicationAsync(intake.ReqId, person.CandidateId, Source, note, ct);
        }
        catch (DependencyUnavailableException)
        {
            await CompensateAsync(intake, "dependency_unavailable", ct);
            throw;
        }

        if (application.Error is { } applicationError)
        {
            await CompensateAsync(intake, applicationError.Code, ct);
            return ApplicationErrors.FromPipeline(applicationError);
        }

        intake.Complete(application.Value!, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(ct);
        return new PublicApplicationResultDto(application.Value!, intake.Position);
    }

    /// <summary>Saga compensation (§6.2): a candidate created by this saga is tombstoned; an existing one is left alone.</summary>
    private async Task CompensateAsync(PublicApplication intake, string code, CancellationToken ct)
    {
        bool? compensated = null;
        if (intake.CandidateCreatedHere && intake.CandidateId is { } candidateId)
        {
            try
            {
                compensated = await candidates.TombstoneAsync(candidateId, $"Careers intake failed: {code}", ct);
            }
            catch (DependencyUnavailableException ex)
            {
                CompensationFailed(logger, intake.Id, ex);
                compensated = false;
            }
        }

        intake.Fail(code, compensated, clock.GetUtcNow());
        await unitOfWork.SaveChangesAsync(CancellationToken.None);
    }

    private async Task FailAsync(PublicApplication intake, string code, CancellationToken ct)
    {
        if (intake.State == IntakeState.Started)
        {
            intake.Fail(code, compensated: null, clock.GetUtcNow());
            await unitOfWork.SaveChangesAsync(ct);
        }
    }

    /// <summary>Hash of the Idempotency-Key and the email, so two applicants never share a key's result.</summary>
    private static string? ClientKey(string? idempotencyKey, string email)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return null;
        }

        var raw = $"{idempotencyKey.Trim()}\u001f{email.Trim().ToUpperInvariant()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Intake {IntakeId}: tombstoning the candidate failed; retention will purge it")]
    private static partial void CompensationFailed(ILogger logger, Guid intakeId, Exception ex);
}

/// <summary>Consent type names, the Candidate service's <c>ConsentType</c> spelling.</summary>
public static class ConsentTypes
{
    public const string DataPrivacy = "DataPrivacy";
    public const string ConflictOfInterest = "ConflictOfInterest";
}

public static class ApplicationErrors
{
    public static readonly Error PostingUnavailable =
        Error.Validation([new FieldError("postingId", "posting_unavailable", "Select a position to apply for.")]);

    public static readonly Error AlreadySubmitted =
        Error.Conflict("already_submitted", "This application is already being processed. Check its status with your Application ID.");

    public static readonly Error PositionClosed =
        Error.Conflict("position_closed", "This position is no longer accepting applications.");

    public static readonly Error AlreadyApplied =
        Error.Conflict("already_applied", "You have already applied for this position.");

    /// <summary>Neutral on purpose (RCU-CAR-005): the same answer for a wrong, unknown or foreign id.</summary>
    public static readonly Error StatusNotFound =
        Error.NotFound("application_not_found", "We couldn't find an application with that ID.");

    /// <summary>Pipeline's refusals in words an applicant can act on; nothing internal leaks.</summary>
    public static Error FromPipeline(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Code switch
        {
            "sourcing_locked" => PositionClosed,
            "already_applied" => AlreadyApplied,
            _ => Error.Conflict("application_not_accepted", "The application could not be accepted. Please try again later."),
        };
    }
}
