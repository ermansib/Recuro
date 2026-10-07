namespace Recuro.BuildingBlocks.Application.IntegrationEvents;

/// <summary>
/// The event catalog (RCU-BKD-001 §5): the only cross-service names that live in shared code.
/// Producers and consumers each declare their own payload record; JSON schemas live in
/// <c>services/contracts/events</c>. A breaking payload change is a new major type (…v2).
/// </summary>
public static class EventTypes
{
    public static class Requisition
    {
        public const string Submitted = "recruitment.mrf.submitted.v1";
        public const string Approved = "recruitment.mrf.approved.v1";
        public const string Rejected = "recruitment.mrf.rejected.v1";
        public const string SourcingUnlocked = "recruitment.sourcing.unlocked.v1";
        public const string Cancelled = "recruitment.mrf.cancelled.v1";
    }

    public static class Candidate
    {
        public const string Created = "candidate.created.v1";
        public const string Merged = "candidate.merged.v1";
        public const string Purged = "candidate.purged.v1";
    }

    public static class Pipeline
    {
        public const string ApplicationCreated = "pipeline.application.created.v1";
        public const string StageChanged = "pipeline.stage.changed.v1";
        public const string FinalRejected = "pipeline.application.final_rejected.v1";
        public const string TatBreached = "pipeline.tat.breached.v1";
    }

    public static class Interview
    {
        public const string Scheduled = "interview.scheduled.v1";
        public const string FeedbackSubmitted = "interview.feedback.submitted.v1";
        public const string FeedbackOverdue = "interview.feedback.overdue.v1";
        public const string SelectionRatified = "interview.selection.ratified.v1";
    }

    public static class Bgv
    {
        public const string CaseInitiated = "bgv.case.initiated.v1";
        public const string CheckUpdated = "bgv.check.updated.v1";
        public const string AdverseFlagged = "bgv.adverse.flagged.v1";
        public const string Cleared = "bgv.cleared.v1";
        public const string Resolved = "bgv.resolved.v1";
    }

    public static class Offer
    {
        public const string Submitted = "offer.submitted.v1";
        public const string Approved = "offer.approved.v1";
        public const string Sent = "offer.sent.v1";
        public const string Accepted = "offer.accepted.v1";
        public const string Declined = "offer.declined.v1";
        public const string Expired = "offer.expired.v1";
    }

    public static class Onboarding
    {
        public const string JoiningInstructionsSent = "onboarding.joining_instructions_sent.v1";
        public const string Day1Ready = "onboarding.day1.ready.v1";
        public const string MilestoneDue = "onboarding.milestone.due.v1";
        public const string EmployeeConfirmed = "onboarding.employee.confirmed.v1";
    }

    public static class Vendor
    {
        public const string Empanelled = "vendor.empanelled.v1";
        public const string DeEmpanelled = "vendor.de_empanelled.v1";
        public const string SlaBreached = "vendor.sla.breached.v1";
    }

    public static class Career
    {
        public const string JobApplied = "career.job.applied.v1";
    }

    public static class Employee
    {
        public const string IjpApplied = "employee.ijp.applied.v1";
        public const string ReferralSubmitted = "employee.referral.submitted.v1";
    }

    public static class Workflow
    {
        public const string TaskCreated = "workflow.task.created.v1";
        public const string TaskCompleted = "workflow.task.completed.v1";
        public const string Escalated = "workflow.escalated.v1";
        public const string SlaPaused = "workflow.sla.paused.v1";
        public const string SlaResumed = "workflow.sla.resumed.v1";
    }

    public static class Config
    {
        public const string VersionActivated = "config.version.activated.v1";
    }

    public static class Identity
    {
        public const string UserProvisioned = "identity.user.provisioned.v1";
        public const string RoleChanged = "identity.role.changed.v1";
    }

    public static class Notification
    {
        public const string Created = "notification.created.v1";
        public const string EmailDispatched = "notification.email.dispatched.v1";
        public const string EmailFailed = "notification.email.failed.v1";
    }

    /// <summary>Subscribes a consumer to every event type (the audit mirror).</summary>
    public const string All = "#";
}
