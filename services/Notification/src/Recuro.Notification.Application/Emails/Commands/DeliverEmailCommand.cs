using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Domain.Emails;

namespace Recuro.Notification.Application.Emails.Commands;

/// <summary>
/// RCU-NTF-002: one delivery attempt for a due email. Suppresses mail to bounced addresses, otherwise
/// sends through the provider; failures retry with exponential backoff and dead-letter after the last
/// attempt. The outcome event leaves through the outbox in the same transaction.
/// </summary>
public sealed record DeliverEmailCommand(Guid EmailId) : ICommand;

internal sealed class DeliverEmailCommandHandler(
    INotificationStore store,
    IEmailTransport transport,
    IUnitOfWork unitOfWork,
    EmailOptions options,
    TimeProvider clock) : ICommandHandler<DeliverEmailCommand>
{
    public async Task<Result> Handle(DeliverEmailCommand command, CancellationToken ct)
    {
        var message = await store.FindEmailAsync(command.EmailId, ct);
        if (message is null)
        {
            return Error.NotFound("email_not_found", $"Email {command.EmailId} was not found.");
        }

        if (message.Status != EmailStatus.Pending)
        {
            return Result.Success();
        }

        var result = await AttemptAsync(message, ct);
        if (result.IsSuccess)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }

        return result;
    }

    private async Task<Result> AttemptAsync(EmailMessage message, CancellationToken ct)
    {
        if ((await store.FindContactAsync(message.ToAddress!, ct))?.Bounced == true)
        {
            return message.Suppress(SuppressionReasons.Bounced, clock.GetUtcNow());
        }

        try
        {
            var providerId = await transport.SendAsync(message, ct);
            return message.MarkSent(providerId, clock.GetUtcNow());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return message.RecordFailure(ex.Message, clock.GetUtcNow(), options.RetryBackoff);
        }
    }
}
