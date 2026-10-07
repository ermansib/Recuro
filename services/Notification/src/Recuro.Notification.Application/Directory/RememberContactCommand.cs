using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Domain.Directory;

namespace Recuro.Notification.Application.Directory;

/// <summary>
/// Records the signed-in person's display name, address and roles from their token, so mail to their
/// role can reach them. Identity's events carry ids and roles only (no PII), so the address is learnt
/// the first time the person opens the portal, the same just-in-time way Identity mirrors them.
/// </summary>
public sealed record RememberContactCommand(string? Name, string? Email) : ICommand;

internal sealed class RememberContactCommandValidator : AbstractValidator<RememberContactCommand>
{
    public RememberContactCommandValidator()
    {
        RuleFor(c => c.Name).MaximumLength(500);
        RuleFor(c => c.Email).MaximumLength(320).EmailAddress().When(c => !string.IsNullOrWhiteSpace(c.Email));
    }
}

internal sealed class RememberContactCommandHandler(
    INotificationStore store,
    IUnitOfWork unitOfWork,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<RememberContactCommand>
{
    public async Task<Result> Handle(RememberContactCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(user.UserId))
        {
            return Result.Success();
        }

        var now = clock.GetUtcNow();
        var entry = await store.FindUserAsync(user.UserId, ct);
        if (entry is null)
        {
            entry = DirectoryUser.Create(user.UserId, now);
            store.Add(entry);
        }
        else if (entry.Name == command.Name && entry.Email == command.Email?.Trim() && entry.Roles.SequenceEqual(user.Roles))
        {
            return Result.Success();
        }

        entry.Update(command.Name, command.Email, [.. user.Roles], now);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
