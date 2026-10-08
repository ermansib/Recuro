using Recuro.BuildingBlocks.Domain;

namespace Recuro.Interview.Domain.Interviews;

public static class InterviewErrors
{
    public static Error NotFound(Guid id) => Error.NotFound("interview_not_found", $"Interview {id} not found.");

    public static Error AssessmentNotFound(Guid id) => Error.NotFound("assessment_not_found", $"Assessment {id} not found.");

    public static Error NoInterview(string appId) => Error.NotFound("interview_not_found", $"No interview for {appId}.");

    public static Error AssessmentLocked(Guid id) =>
        Error.Conflict("assessment_locked", $"Assessment {id} is submitted and locked. Supersede it with a reason to change it.");

    public static Error NotSubmitted(Guid id) =>
        Error.Conflict("assessment_not_submitted", $"Assessment {id} is not submitted yet; edit the draft instead.");

    public static Error RoundClosed(Guid id, InterviewStatus status) =>
        Error.Conflict("interview_closed", $"Interview {id} is {status}.");

    public static Error StaleVersion(Guid id) =>
        Error.Conflict("stale_version", $"Assessment {id} was changed by someone else. Reload it and try again.");

    public static Error NotInInterviewStage(string appId) =>
        Error.Conflict("application_not_in_interview", $"{appId} is not at the Interview stage.");

    public static Error JobDescriptionMissing(string reqId) =>
        Error.Conflict("job_description_missing", $"{reqId} has no job description, so there are no competencies to assess.");

    public static Error RoundNotAllowed(string roundType, string grade) =>
        Error.Validation([new FieldError("roundType", "round_not_allowed", $"Round type {roundType} is not in the {grade} interview template.")]);

    public static Error NoTemplate(string grade) =>
        Error.Validation([new FieldError("grade", "no_round_template", $"No interview template is configured for grade {grade}.")]);

    public static Error ReasonRequired() =>
        Error.Validation([new FieldError("reason", "reason_required", $"A documented reason of at least {InterviewLimits.MinReasonLength} characters is mandatory.")]);

    public static Error Invalid(IEnumerable<(string Field, string Code, string Message)> problems) =>
        Error.Validation(problems.Select(p => new FieldError(p.Field, p.Code, p.Message)).ToList());

    public static Error SelectionIncomplete(string appId) =>
        Error.Conflict("selection_incomplete", $"Rounds for {appId} are still waiting for feedback.");

    public static Error SelectionAlreadySubmitted(string appId) =>
        Error.Conflict("selection_already_submitted", $"The selection summary for {appId} is already submitted.");
}
