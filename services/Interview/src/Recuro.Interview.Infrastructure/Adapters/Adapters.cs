using Microsoft.Extensions.Logging;
using Recuro.BuildingBlocks.Infrastructure.Documents;
using Recuro.Interview.Application.Abstractions;
using Recuro.Interview.Application.Selection;

namespace Recuro.Interview.Infrastructure.Adapters;

/// <summary>
/// Default calendar adapter: logs the invite (ids and times only, no personal data). A tenant's calendar
/// (CalDAV, Google, Microsoft 365) plugs in behind <see cref="ICalendarInvites"/>.
/// </summary>
internal sealed partial class LoggingCalendarInvites(ILogger<LoggingCalendarInvites> logger) : ICalendarInvites
{
    public Task SendAsync(CalendarInvite invite, CancellationToken ct)
    {
        Sent(logger, invite.InterviewId, invite.Start, invite.End, invite.AttendeeIds.Count);
        return Task.CompletedTask;
    }

    public Task CancelAsync(CalendarInvite invite, CancellationToken ct)
    {
        Cancelled(logger, invite.InterviewId, invite.Start);
        return Task.CompletedTask;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Calendar invite for interview {InterviewId}: {Start} to {End}, {Attendees} attendees")]
    private static partial void Sent(ILogger logger, Guid interviewId, DateTimeOffset start, DateTimeOffset end, int attendees);

    [LoggerMessage(Level = LogLevel.Information, Message = "Calendar invite for interview {InterviewId} at {Start} cancelled")]
    private static partial void Cancelled(ILogger logger, Guid interviewId, DateTimeOffset start);
}

internal sealed class SimplePdfRenderer : IPdfRenderer
{
    public byte[] Render(string title, IReadOnlyList<string> lines) => SimplePdf.Render(title, lines);
}
