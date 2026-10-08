using System.Globalization;
using Microsoft.Extensions.Logging;
using Recuro.BuildingBlocks.Infrastructure.Documents;
using Recuro.Onboarding.Application.Abstractions;
using Recuro.Onboarding.Domain.Cases;

namespace Recuro.Onboarding.Infrastructure.Adapters;

/// <summary>
/// RCU-ONB-001 stand-in for a tenant's IT/Admin ticketing system: it logs the request and returns a
/// stable ticket reference per case. A helpdesk adapter (for example a free, self-hosted one such as
/// Zammad or GLPI) replaces it behind <see cref="IProvisioningAdapter"/> without touching use cases.
/// </summary>
internal sealed partial class LoggingProvisioningAdapter(ILogger<LoggingProvisioningAdapter> logger) : IProvisioningAdapter
{
    public Task<string> OpenTicketAsync(ProvisioningRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var ticket = TicketFor(request.CaseId);
        TicketOpened(logger, ticket, request.CaseId, request.AppId, request.JoiningDate);
        return Task.FromResult(ticket);
    }

    public Task CancelTicketAsync(string ticketRef, CancellationToken ct)
    {
        TicketCancelled(logger, ticketRef);
        return Task.CompletedTask;
    }

    /// <summary>Same case, same ticket: a retried scan never opens a second one.</summary>
    internal static string TicketFor(Guid caseId) => string.Create(CultureInfo.InvariantCulture, $"IT-{caseId.ToString("N")[^8..].ToUpperInvariant()}");

    [LoggerMessage(Level = LogLevel.Information, Message = "IT/Admin Day-1 ticket {Ticket} opened for onboarding case {CaseId} (application {AppId}, joining {JoiningDate})")]
    private static partial void TicketOpened(ILogger logger, string ticket, Guid caseId, string appId, DateOnly joiningDate);

    [LoggerMessage(Level = LogLevel.Information, Message = "IT/Admin ticket {Ticket} cancelled: the hire will not join")]
    private static partial void TicketCancelled(ILogger logger, string ticket);
}

/// <summary>
/// RCU-ONB-005: the confirmation letter as a PDF. The issuing company's name and branding are the
/// tenant's (white-label), so the letter names none.
/// </summary>
internal sealed class PdfConfirmationLetterRenderer : IConfirmationLetterRenderer
{
    public byte[] Render(OnboardingCase onboardingCase, ProbationDecision decision, string employeeName)
    {
        ArgumentNullException.ThrowIfNull(onboardingCase);
        ArgumentNullException.ThrowIfNull(decision);
        var lines = new List<string>
        {
            Format($"Date: {decision.DecidedAt:dd MMM yyyy} · Application {onboardingCase.AppId} · Requisition {onboardingCase.ReqId}"),
            string.Empty,
            $"Dear {employeeName},",
            string.Empty,
            Format($"We are pleased to confirm your employment with effect from {decision.DecidedAt:dd MMM yyyy}, on successful completion of your probation (joined {onboardingCase.JoiningDate:dd MMM yyyy})."),
            string.Empty,
            "All other terms and conditions of your appointment remain unchanged.",
            string.Empty,
            "We thank you for your contribution and wish you every success.",
            string.Empty,
            "Human Resources",
        };
        return SimplePdf.Render("Confirmation of Employment", lines);
    }

    private static string Format(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
