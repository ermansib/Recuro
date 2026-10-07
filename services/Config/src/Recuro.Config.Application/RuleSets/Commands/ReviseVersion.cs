using System.Text.Json;
using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Config.Application.Abstractions;
using Recuro.Config.Domain.RuleSets;

namespace Recuro.Config.Application.RuleSets.Commands;

/// <summary>The proposer changes their draft before anyone approves it.</summary>
public sealed record ReviseVersionCommand(MatrixType MatrixType, Guid Id, DateTimeOffset? EffectiveFrom, JsonElement Content, string? Note) : ICommand<RuleSetVersionDto>;

internal sealed class ReviseVersionCommandValidator : AbstractValidator<ReviseVersionCommand>
{
    public ReviseVersionCommandValidator()
    {
        RuleFor(c => c.EffectiveFrom).NotNull();
        RuleFor(c => c.Note).MaximumLength(ConfigLimits.NoteLength);
        this.ValidContent(c => c.MatrixType, c => c.Content);
    }
}

internal sealed class ReviseVersionCommandHandler(IRuleSetVersions versions, ICurrentUser user) : ICommandHandler<ReviseVersionCommand, RuleSetVersionDto>
{
    public async Task<Result<RuleSetVersionDto>> Handle(ReviseVersionCommand command, CancellationToken ct)
    {
        var version = await versions.GetAsync(command.Id, ct);
        if (version is null || version.MatrixType != command.MatrixType)
        {
            return RuleSetErrors.NotFound(command.MatrixType, command.Id);
        }

        MatrixJson.TryParse(command.MatrixType, command.Content, out var matrix, out _);
        var revised = version.Revise(MatrixJson.Write(matrix!), command.EffectiveFrom!.Value, command.Note, user.UserId!);
        if (revised.IsFailure)
        {
            return revised.Error!;
        }

        return await versions.TrySaveAsync(ct)
            ? RuleSetVersionDto.From(version, [])
            : RuleSetErrors.ChangedConcurrently;
    }
}
