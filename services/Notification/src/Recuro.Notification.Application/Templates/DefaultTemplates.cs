using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Domain.Templates;

namespace Recuro.Notification.Application.Templates;

/// <summary>
/// Recuro's default English templates, one per matrix rule. Tenants override any of them by key from
/// the admin portal (RCU-CFG-004); this catalog is the fallback. Text names entities by id only:
/// events carry ids, not names or contact details.
/// </summary>
public sealed class DefaultTemplates : ITemplateSource
{
    public const string Version = "default-2026.10.1";

    private static readonly IReadOnlyDictionary<string, NotificationTemplate> Catalog = new[]
    {
        T("approval.task.assigned", "📋", "Approval needed — {subjectId}", "{type} task waiting for your decision.", "/approvals",
            "Approval", "Approval needed — {subjectId}",
            ["A {type} approval is waiting for you ({subjectId}).", "Please decide within the SLA. Breaches escalate automatically per the escalation matrix."],
            "Open Approvals"),
        T("mrf.approved", "✅", "MRF approved — {reqId}", "Sourcing is unlocked.", "/mrf",
            "Approved", "✅ MRF approved — {reqId}",
            ["Your requisition {reqId} has been approved.", "Sourcing is now unlocked."], "Open Requisition"),
        T("mrf.rejected", "✖", "MRF rejected — {reqId}", "Reason: {reason}", "/mrf",
            "Rejected", "MRF rejected — {reqId}",
            ["Your requisition {reqId} was rejected.", "Reason given: {reason}"], "Open Requisition"),
        T("mrf.cancelled", "⊘", "MRF cancelled — {reqId}", "Reason: {reason}", "/mrf",
            "Cancelled", "MRF cancelled — {reqId}", ["Requisition {reqId} was cancelled.", "Reason given: {reason}"], null),
        T("workflow.escalated", "⚑", "Escalation — {subjectId}", "{type} SLA breached · your decision is needed.", "/approvals",
            "Escalation", "⚑ Escalation — {subjectId}",
            ["The {type} step for {subjectId} has breached its SLA and has been escalated to you."], "Open Approvals"),
        T("tat.breached", "⚑", "TAT breach — {entity}", "{stage} · variance {variance} · escalation {escalationPath}", "/",
            "Escalation", "⚑ TAT breach — {stage}, {entity}",
            ["{entity} has exceeded the {stage} TAT (variance {variance}).", "Escalation path: {escalationPath}."], "Open Dashboard"),
        T("candidate.regret", "✉", "Application update — {appId}", "Regret email scheduled.", null,
            "Regret", "Your application {appId}",
            ["Thank you for your interest and for the time you gave us.", "After careful consideration we will not be moving forward with your application {appId} on this occasion.", "We will keep your details for future roles in line with our privacy notice."],
            null),
        T("application.created", "📥", "New application — {reqId}", "{appId} via {source} · auto-logged to the pipeline.", "/pipeline",
            "Application", "New application — {reqId}", ["Application {appId} was received via {source}."], "Open Pipeline"),
        T("career.applied.confirmation", "✅", "Application received — {jobId}", "{appId} · under HR review.", "/careers",
            "Confirmation", "We received your application ({appId})",
            ["Thank you for applying. Your application {appId} is under review.", "We will contact you about next steps."], "Track Application"),
        T("interview.scheduled", "📅", "Interview scheduled — {appId}", "Round {roundId} · {dueAt}", "/assessment",
            "Interview", "📅 Interview scheduled — {appId}",
            ["You are on the panel for round {roundId} of application {appId}.", "Scheduled for {dueAt}. A calendar invite follows separately."], "Open Assessment Form"),
        T("interview.feedback.overdue", "⏱", "Panel feedback overdue — {appId}", "Round {roundId} · due {dueAt}", "/assessment",
            "Reminder", "⏰ Panel feedback overdue — {appId}",
            ["Feedback for round {roundId} of application {appId} was due {dueAt}.", "Outstanding feedback escalates to the HOD per the escalation matrix."], "Open Assessment Form"),
        T("interview.selection.ratified", "🏁", "Selection ratified — {appId}", "Average score {avg}.", "/pipeline",
            "Selection", "Selection ratified — {appId}", ["The selection for {appId} was ratified (average {avg})."], null),
        T("bgv.cleared", "🛡", "BGV cleared — {appId}", "All checks cleared · offer can be released.", "/bgv",
            "BGV", "BGV cleared — {appId}", ["Background verification for {appId} is cleared."], "Open BGV"),
        T("bgv.adverse.flagged", "⚑", "Adverse BGV — {appId}", "{checkType} · offer on hold · action required.", "/approvals",
            "Escalation", "⚑ Adverse BGV finding — {appId}",
            ["An adverse finding was flagged on the {checkType} check for {appId}.", "The offer is on hold until the case is resolved."], "Open Approvals"),
        T("offer.approved", "✅", "Offer approved — {offerId}", "Release after BGV clearance.", "/offer",
            "Approved", "✅ Offer approved — {offerId}", ["Offer {offerId} for {appId} is approved.", "Release it after BGV clearance."], "Open Offer"),
        T("offer.accepted", "🎉", "Offer accepted — {offerId}", "{appId} · onboarding starts.", "/offer",
            "Offer", "Offer accepted — {offerId}", ["Offer {offerId} was accepted."], null),
        T("offer.declined", "✉", "Offer declined — {offerId}", "Acknowledgement needed.", "/approvals",
            "Offer", "Offer declined — {offerId}", ["Offer {offerId} for {appId} was declined."], "Open Approvals"),
        T("ijp.applied", "🔁", "IJP application — {reqId}", "Employee {employeeId} applied internally.", "/pipeline",
            "IJP", "IJP application — {reqId}", ["An internal application was submitted for {reqId}."], "Open Pipeline"),
        T("ijp.applied.confirmation", "✅", "IJP application received — {reqId}", "HR-TA will review it.", "/internal-careers",
            "Confirmation", "Your internal application for {reqId}", ["Thank you. Your internal application for {reqId} was received."], null),
        T("referral.submitted", "🤝", "New referral — {reqId}", "Referred by {referrerId}.", "/pipeline",
            "Referral", "New referral — {reqId}", ["A referral was submitted for {reqId}."], "Open Pipeline"),
        T("referral.submitted.confirmation", "✅", "Referral received — {reqId}", "Thank you for your referral.", "/internal-careers",
            "Confirmation", "Your referral for {reqId}", ["Thank you. Your referral for {reqId} was received."], null),
        T("vendor.sla.breached", "⚠", "Vendor SLA breach — {vendorId}", "Review empanelment.", "/vendors",
            "Vendor", "Vendor SLA breach — {vendorId}", ["Vendor {vendorId} has breached its SLA."], "Open Vendors"),
    }.ToDictionary(t => t.Key, StringComparer.Ordinal);

    public static IReadOnlyCollection<string> Keys => (IReadOnlyCollection<string>)Catalog.Keys;

    public Task<NotificationTemplate?> GetAsync(string key, CancellationToken ct) =>
        Task.FromResult(Catalog.GetValueOrDefault(key));

    private static NotificationTemplate T(
        string key,
        string icon,
        string title,
        string body,
        string? link,
        string tag,
        string subject,
        string[] paragraphs,
        string? cta) =>
        new(key, Version, icon, title, body, link, tag, subject, paragraphs, cta);
}
