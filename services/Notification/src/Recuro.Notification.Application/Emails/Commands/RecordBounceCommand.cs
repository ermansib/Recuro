using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Domain.Emails;

namespace Recuro.Notification.Application.Emails.Commands;

/// <summary>
/// RCU-NTF-002 bounce handling: the mail provider (or its webhook adapter) reports a hard bounce, and
/// the address stops receiving mail; later emails to it are logged as suppressed.
/// </summary>
public sealed record RecordBounceCommand(string Address, string? Reason) : ICommand;

internal sealed class RecordBounceCommandValidator : AbstractValidator<RecordBounceCommand>
{
    public RecordBounceCommandValidator()
    {
        RuleFor(c => c.Address).NotEmpty().EmailAddress().MaximumLength(320);
        RuleFor(c => c.Reason).MaximumLength(500);
    }
}

internal sealed class RecordBounceCommandHandler(INotificationStore store, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<RecordBounceCommand>
{
    public async Task<Result> Handle(RecordBounceCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var contact = await store.FindContactAsync(command.Address, ct);
        if (contact is null)
        {
            contact = ContactStatus.ForAddress(command.Address, now);
            store.Add(contact);
        }

        contact.RecordBounce(command.Reason ?? "hard_bounce", now);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
