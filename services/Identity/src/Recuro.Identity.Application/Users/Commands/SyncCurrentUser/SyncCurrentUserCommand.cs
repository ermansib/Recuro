using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Identity.Application.Abstractions;
using Recuro.Identity.Domain.Users;

namespace Recuro.Identity.Application.Users.Commands.SyncCurrentUser;

/// <summary>
/// RCU-AUT-001: provisions the signed-in person just in time, or refreshes their mirror from the
/// token, and returns them. The values come from the validated token, never from the request body.
/// </summary>
public sealed record SyncCurrentUserCommand(string Subject, string? Name, string? Email, IReadOnlyList<string> Roles) : ICommand<UserDto>;

internal sealed class SyncCurrentUserCommandValidator : AbstractValidator<SyncCurrentUserCommand>
{
    public SyncCurrentUserCommandValidator()
    {
        RuleFor(c => c.Subject).NotEmpty().MaximumLength(UserLimits.SubjectLength);
        RuleFor(c => c.Name).MaximumLength(UserLimits.NameLength);
        RuleFor(c => c.Email).MaximumLength(UserLimits.EmailLength);
    }
}

internal sealed class SyncCurrentUserCommandHandler(
    IUserAccounts users,
    ITenantContext tenant,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<SyncCurrentUserCommand, UserDto>
{
    public async Task<Result<UserDto>> Handle(SyncCurrentUserCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var existing = await users.FindBySubjectAsync(command.Subject, ct);
        if (existing is null)
        {
            var created = UserAccount.Provision(tenant.RequiredTenantId, command.Subject, command.Name ?? string.Empty, command.Email ?? string.Empty, command.Roles, now);
            if (await users.TryAddAsync(created, ct))
            {
                return UserDto.From(created);
            }

            // Someone else provisioned this person a moment ago (parallel first requests).
            existing = await users.FindBySubjectAsync(command.Subject, ct)
                ?? throw new InvalidOperationException("The user was provisioned concurrently but cannot be read.");
        }

        existing.SyncFromToken(command.Name ?? string.Empty, command.Email ?? string.Empty, command.Roles, now);
        await unitOfWork.SaveChangesAsync(ct);
        return UserDto.From(existing);
    }
}
