namespace Recuro.Notification.Domain.Matrix;

/// <summary>Where a notification goes. A rule can use both.</summary>
[Flags]
public enum Channels
{
    None = 0,
    InApp = 1,
    Email = 2,
}

/// <summary>How a rule finds its recipients (FRD §5.6 "role-in-context").</summary>
public enum RecipientKind
{
    /// <summary>Everyone holding <see cref="RecipientRule.Role"/> in the tenant.</summary>
    Role,

    /// <summary>The user who caused the event (CloudEvents <c>actorid</c>), e.g. the applicant.</summary>
    Actor,

    /// <summary>A user id in a payload field, e.g. <c>assignee</c>.</summary>
    PayloadUser,

    /// <summary>A list of user ids in a payload field, e.g. <c>panel</c>.</summary>
    PayloadUsers,

    /// <summary>Whoever started the subject's flow, e.g. the MRF initiator (see <see cref="NotificationMatrix.OwnerEvents"/>).</summary>
    SubjectOwner,

    /// <summary>A candidate record named in a payload field (not a portal user); the address comes from Candidate.</summary>
    PayloadCandidate,
}

/// <summary>
/// One recipient of a rule. <see cref="Role"/> is the role key the person receives it in (the frontend's
/// <c>recipientRole</c>); for <see cref="RecipientKind.Role"/> it is also who receives it. When a user
/// cannot be resolved and <see cref="FallbackToRole"/> is set, everyone in <see cref="Role"/> gets it instead.
/// </summary>
public sealed record RecipientRule(RecipientKind Kind, string Role, string? Field = null, bool FallbackToRole = false)
{
    public static RecipientRule ForRole(string role) => new(RecipientKind.Role, role);

    public static RecipientRule ForActor(string role) => new(RecipientKind.Actor, role);

    public static RecipientRule ForPayloadUser(string field, string role, bool fallbackToRole = false) =>
        new(RecipientKind.PayloadUser, role, field, fallbackToRole);

    public static RecipientRule ForPayloadUsers(string field, string role) => new(RecipientKind.PayloadUsers, role, field);

    public static RecipientRule ForSubjectOwner(string role) => new(RecipientKind.SubjectOwner, role);

    public static RecipientRule ForPayloadCandidate(string field) => new(RecipientKind.PayloadCandidate, "candidate", field);
}

/// <summary>
/// One row of the event matrix: when <see cref="EventType"/> arrives, render <see cref="TemplateKey"/>
/// for <see cref="Recipients"/> on <see cref="Channels"/>. Critical rules (approvals, escalations,
/// adverse findings) are never muted by preferences (RCU-NTF-005). <see cref="SendAtField"/> names a
/// payload date that schedules the email instead of sending it at once.
/// </summary>
public sealed record MatrixRule(string EventType, string TemplateKey, Channels Channels, bool Critical, IReadOnlyList<RecipientRule> Recipients, string? SendAtField = null);

/// <summary>
/// The FRD §5.6 notification and email event matrix as data (RCU-NTF-001). Changing who hears about
/// what is an edit to <see cref="Rules"/>, not new code; the version is stamped on every delivery so
/// the log shows which matrix sent it.
/// </summary>
public static class NotificationMatrix
{
    public const string Version = "ntf-matrix-2026.10.3";

    // Role keys: the same strings as the frontend Role type and the Keycloak realm roles.
    private const string HrTa = "hrta";
    private const string HrHead = "hrhead";
    private const string MdCeo = "mdceo";
    private const string Employee = "employee";
    private const string Candidate = "candidate";

    private const Channels Both = Channels.InApp | Channels.Email;

    /// <summary>Roles that may receive role-wide broadcasts. Candidates and employees only ever get their own items.</summary>
    public static readonly IReadOnlySet<string> BroadcastRoles = new HashSet<string>(StringComparer.Ordinal) { HrTa, HrHead, MdCeo };

    /// <summary>Every role a notification can be addressed in.</summary>
    public static readonly IReadOnlySet<string> KnownRoles = new HashSet<string>(StringComparer.Ordinal) { HrTa, HrHead, MdCeo, Employee, Candidate };

    /// <summary>Events whose actor becomes the subject's owner (for <see cref="RecipientKind.SubjectOwner"/>), e.g. the MRF initiator.</summary>
    public static readonly IReadOnlySet<string> OwnerEvents = new HashSet<string>(StringComparer.Ordinal)
    {
        "recruitment.mrf.submitted.v1",
    };

    public static readonly IReadOnlyList<MatrixRule> Rules =
    [
        // §5.6 #1: the approving leg hears about its task (Workflow names the assignee).
        new("workflow.task.created.v1", "approval.task.assigned", Both, Critical: true, [RecipientRule.ForPayloadUser("assignee", HrHead, fallbackToRole: true)]),

        // RCU-WFL-003: reminders at 50% and 100% of the task's SLA go to its assignees (named users, or the role
        // Workflow assigned it to in assigneeRole); escalation stays workflow.escalated.
        new("workflow.task.reminder_due.v1", "approval.task.reminder", Both, Critical: false,
            [RecipientRule.ForPayloadUsers("assigneeIds", HrHead), RecipientRule.ForPayloadUser("assigneeRole", HrHead)]),

        // §5.6 #2: the initiator hears the outcome; the reason is quoted on reject.
        new("recruitment.mrf.approved.v1", "mrf.approved", Both, Critical: true, [RecipientRule.ForSubjectOwner(HrTa)]),
        new("recruitment.mrf.rejected.v1", "mrf.rejected", Both, Critical: true, [RecipientRule.ForSubjectOwner(HrTa)]),
        new("recruitment.mrf.cancelled.v1", "mrf.cancelled", Channels.InApp, Critical: false, [RecipientRule.ForRole(HrTa)]),

        // §5.6 #3 and §16: escalations go to whoever Workflow escalated to, else HR Head.
        new("workflow.escalated.v1", "workflow.escalated", Both, Critical: true, [RecipientRule.ForPayloadUser("escalateTo", HrHead, fallbackToRole: true)]),
        new("pipeline.tat.breached.v1", "tat.breached", Both, Critical: true, [RecipientRule.ForRole(HrTa)]),

        // §5.6 #9 / RCU-CAR-006: the regret email goes out on the date Pipeline set (≤ 3 working days).
        new("pipeline.application.final_rejected.v1", "candidate.regret", Channels.Email, Critical: false, [RecipientRule.ForPayloadCandidate("candidateId")], SendAtField: "regretSendAt"),

        // New applications land with HR-TA; the applicant (a candidate record, no portal account) gets an email confirmation.
        new("pipeline.application.created.v1", "application.created", Channels.InApp, Critical: false, [RecipientRule.ForRole(HrTa)]),
        new("career.job.applied.v1", "career.applied.confirmation", Channels.Email, Critical: false, [RecipientRule.ForPayloadCandidate("candidateId")]),

        // §5.6 #5–#8: interviews.
        new("interview.scheduled.v1", "interview.scheduled", Both, Critical: false, [RecipientRule.ForPayloadUsers("panel", HrTa)]),
        // RCU-INT-004: a 24h nudge to interviewers who haven't submitted, then the 48h overdue escalation.
        new("interview.feedback.reminder_due.v1", "interview.feedback.reminder", Both, Critical: false, [RecipientRule.ForPayloadUsers("pendingInterviewerIds", HrTa)]),
        new("interview.feedback.overdue.v1", "interview.feedback.overdue", Both, Critical: true, [RecipientRule.ForPayloadUsers("panel", HrTa), RecipientRule.ForRole(HrTa)]),
        new("interview.selection.ratified.v1", "interview.selection.ratified", Channels.InApp, Critical: false, [RecipientRule.ForRole(HrTa)]),

        // §5.6 #11–#12: BGV.
        new("bgv.cleared.v1", "bgv.cleared", Channels.InApp, Critical: false, [RecipientRule.ForRole(HrTa)]),
        new("bgv.adverse.flagged.v1", "bgv.adverse.flagged", Both, Critical: true, [RecipientRule.ForRole(HrHead), RecipientRule.ForRole(MdCeo)]),

        // §5.6 #13–#15: offers.
        new("offer.approved.v1", "offer.approved", Both, Critical: true, [RecipientRule.ForRole(HrTa)]),
        new("offer.accepted.v1", "offer.accepted", Channels.InApp, Critical: false, [RecipientRule.ForRole(HrTa)]),
        new("offer.declined.v1", "offer.declined", Channels.InApp, Critical: false, [RecipientRule.ForRole(HrHead)]),

        // RCU-OFR-006: an unanswered offer chases the candidate by email and tells HR-TA in the bell.
        new("offer.chase_due.v1", "offer.chase.candidate", Channels.Email, Critical: false, [RecipientRule.ForPayloadCandidate("candidateId")]),
        new("offer.chase_due.v1", "offer.chase", Channels.InApp, Critical: false, [RecipientRule.ForRole(HrTa)]),

        // §5.6 #16: internal mobility and referrals: HR-TA gets the lead, the employee a confirmation.
        new("employee.ijp.applied.v1", "ijp.applied", Both, Critical: false, [RecipientRule.ForRole(HrTa)]),
        new("employee.ijp.applied.v1", "ijp.applied.confirmation", Both, Critical: false, [RecipientRule.ForActor(Employee)]),
        new("employee.referral.submitted.v1", "referral.submitted", Both, Critical: false, [RecipientRule.ForRole(HrTa)]),
        new("employee.referral.submitted.v1", "referral.submitted.confirmation", Both, Critical: false, [RecipientRule.ForActor(Employee)]),

        // §5.6 #17: vendor SLA roll-up.
        new("vendor.sla.breached.v1", "vendor.sla.breached", Both, Critical: false, [RecipientRule.ForRole(HrHead)]),
    ];

    /// <summary>Every event type the service subscribes to.</summary>
    public static IEnumerable<string> SubscribedEventTypes =>
        Rules.Select(r => r.EventType).Concat(OwnerEvents).Distinct(StringComparer.Ordinal);

    public static IReadOnlyList<MatrixRule> RulesFor(string eventType) =>
        Rules.Where(r => string.Equals(r.EventType, eventType, StringComparison.Ordinal)).ToList();
}
