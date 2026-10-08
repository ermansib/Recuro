using Recuro.BuildingBlocks.Domain;
using Recuro.Notification.Domain.Emails;
using Recuro.Notification.Domain.Feed;

namespace Recuro.Notification.UnitTests;

public class EmailMessageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 9, 0, 0, TimeSpan.Zero);
    private static readonly EmailSender Sender = new("no-reply@recuro.local", "Recuro", "Recuro (automated)");
    private static readonly RenderedEmail Content = new("Approved", "Subject", ["Hello"], "Open", "/mrf");
    private static readonly DeliveryOrigin Origin = new(Guid.NewGuid(), "offer.approved.v1", "offer.approved", "v1", "m1", Critical: true);

    private static EmailMessage NewMessage(string? email = "a.sharma@example.test") =>
        EmailMessage.Create(new Recipient("hrta", "u-1", "A. Sharma", email), Content, Sender, Origin, Now).Value;

    [Fact]
    public void A_new_email_is_pending_and_due_now()
    {
        var message = NewMessage();

        Assert.Equal(EmailStatus.Pending, message.Status);
        Assert.True(message.IsDue(Now));
    }

    [Fact]
    public void Without_an_address_it_is_logged_as_suppressed()
    {
        var message = NewMessage(email: null);

        Assert.Equal(EmailStatus.Suppressed, message.Status);
        Assert.Equal(SuppressionReasons.NoAddress, message.LastError);
    }

    [Fact]
    public void Sending_records_the_provider_id_and_raises_dispatched()
    {
        var message = NewMessage();

        Assert.True(message.MarkSent("<id@mail>", Now).IsSuccess);

        Assert.Equal(EmailStatus.Sent, message.Status);
        Assert.Equal("<id@mail>", message.ProviderMessageId);
        Assert.IsType<EmailDispatched>(Assert.Single(message.DomainEvents));
    }

    [Fact]
    public void Failures_back_off_exponentially_then_dead_letter_after_three_attempts()
    {
        var message = NewMessage();
        var unit = TimeSpan.FromMinutes(1);

        message.RecordFailure("timeout", Now, unit);
        Assert.Equal(Now + TimeSpan.FromMinutes(1), message.NextAttemptAt);
        message.RecordFailure("timeout", Now, unit);
        Assert.Equal(Now + TimeSpan.FromMinutes(2), message.NextAttemptAt);
        Assert.Equal(EmailStatus.Pending, message.Status);
        Assert.Empty(message.DomainEvents);

        message.RecordFailure("timeout", Now, unit);

        Assert.Equal(EmailStatus.Failed, message.Status);
        Assert.Equal(EmailMessage.MaxAttempts, message.Attempts);
        Assert.IsType<EmailFailed>(Assert.Single(message.DomainEvents));
    }

    [Fact]
    public void A_sent_email_cannot_be_sent_or_failed_again()
    {
        var message = NewMessage();
        message.MarkSent("<id@mail>", Now);

        var again = message.MarkSent("<other@mail>", Now);
        var failed = message.RecordFailure("late", Now, TimeSpan.FromMinutes(1));

        Assert.Equal(ErrorType.Conflict, again.Error!.Type);
        Assert.Equal(ErrorType.Conflict, failed.Error!.Type);
        Assert.Equal("<id@mail>", message.ProviderMessageId);
    }

    [Fact]
    public void Scheduled_mail_waits_until_its_send_time()
    {
        var sendAt = Now.AddDays(3);
        var message = EmailMessage.Create(new Recipient("hrta", "u-1", null, "x@example.test"), Content, Sender, Origin, Now, sendAt).Value;

        Assert.False(message.IsDue(Now));
        Assert.True(message.IsDue(sendAt));
    }

    [Fact]
    public void Long_errors_are_truncated()
    {
        var message = NewMessage();

        message.RecordFailure(new string('x', 2000), Now, TimeSpan.FromMinutes(1));

        Assert.Equal(500, message.LastError!.Length);
    }
}
