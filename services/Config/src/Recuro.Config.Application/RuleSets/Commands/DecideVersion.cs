using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Config.Application.Abstractions;
using Recuro.Config.Domain.RuleSets;

namespace Recuro.Config.Application.RuleSets.Commands;

/// <summary>RCU-CFG-001 dual approval: a second person activates the draft, which emits config.version.activated.v1.</summary>
public sealed record ApproveVersionCommand(MatrixType MatrixType, Guid Id) : ICommand<RuleSetVersionDto>;

/// <summary>A reviewer turns the draft down, with a reason.</summary>
public sealed record RejectVersionCommand(MatrixType MatrixType, Guid Id, string Reason) : ICommand<RuleSetVersionDto>;

internal sealed class RejectVersionCommandValidator : AbstractValidator<RejectVersionCommand>
{
    public RejectVersionCommandValidator() =>
        RuleFor(c => c.Reason).NotEmpty().MinimumLength(ConfigLimits.ReasonMinLength).MaximumLength(ConfigLimits.ReasonLength);
}

internal sealed class ApproveVersionCommandHandler(IRuleSetVersions versions, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<ApproveVersionCommand, RuleSetVersionDto>
{
    public async Task<Result<RuleSetVersionDto>> Handle(ApproveVersionCommand command, CancellationToken ct)
    {
        var version = await versions.GetAsync(command.Id, ct);
        if (version is null || version.MatrixType != command.MatrixType)
        {
            return RuleSetErrors.NotFound(command.MatrixType, command.Id);
        }

        var approved = version.Approve(user.UserId!, user.Name ?? user.UserId!, clock.GetUtcNow());
        if (approved.IsFailure)
        {
            return approved.Error!;
        }

        if (!await versions.TrySaveAsync(ct))
        {
            return RuleSetErrors.ChangedConcurrently;
        }

        return RuleSetVersionDto.From(version, await versions.ListAsync(command.MatrixType, ct));
    }
}

internal sealed class RejectVersionCommandHandler(IRuleSetVersions versions, ICurrentUser user, TimeProvider clock)
    : ICommandHandler<RejectVersionCommand, RuleSetVersionDto>
{
    public async Task<Result<RuleSetVersionDto>> Handle(RejectVersionCommand command, CancellationToken ct)
    {
        var version = await versions.GetAsync(command.Id, ct);
        if (version is null || version.MatrixType != command.MatrixType)
        {
            return RuleSetErrors.NotFound(command.MatrixType, command.Id);
        }

        var rejected = version.Reject(user.UserId!, user.Name ?? user.UserId!, command.Reason, clock.GetUtcNow());
        if (rejected.IsFailure)
        {
            return rejected.Error!;
        }

        return await versions.TrySaveAsync(ct)
            ? RuleSetVersionDto.From(version, [])
            : RuleSetErrors.ChangedConcurrently;
    }
}

internal static class RuleSetErrors
{
    public static readonly Error ChangedConcurrently =
        Error.Conflict("version_changed", "Someone else changed this version at the same time. Reload it and try again.");

    public static Error NotFound(MatrixType type, Guid id) =>
        Error.NotFound("config_version_not_found", $"No {type.ToKey()} version {id}.");
}
