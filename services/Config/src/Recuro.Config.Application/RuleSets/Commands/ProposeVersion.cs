using System.Text.Json;
using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Config.Application.Abstractions;
using Recuro.Config.Domain.RuleSets;

namespace Recuro.Config.Application.RuleSets.Commands;

/// <summary>RCU-CFG-001: propose a new version of a matrix. It stays a draft until a second person approves it.</summary>
public sealed record ProposeVersionCommand(MatrixType MatrixType, DateTimeOffset? EffectiveFrom, JsonElement Content, string? Note) : ICommand<RuleSetVersionDto>;

internal sealed class ProposeVersionCommandValidator : AbstractValidator<ProposeVersionCommand>
{
    public ProposeVersionCommandValidator()
    {
        RuleFor(c => c.EffectiveFrom).NotNull();
        RuleFor(c => c.Note).MaximumLength(ConfigLimits.NoteLength);
        this.ValidContent(c => c.MatrixType, c => c.Content);
    }
}

internal sealed class ProposeVersionCommandHandler(
    IRuleSetVersions versions,
    ICurrentUser user,
    TimeProvider clock) : ICommandHandler<ProposeVersionCommand, RuleSetVersionDto>
{
    public async Task<Result<RuleSetVersionDto>> Handle(ProposeVersionCommand command, CancellationToken ct)
    {
        MatrixJson.TryParse(command.MatrixType, command.Content, out var matrix, out _);
        var content = MatrixJson.Write(matrix!);
        var now = clock.GetUtcNow();
        var created = await versions.AddNextAsync(
            command.MatrixType,
            number => RuleSetVersion.Propose(command.MatrixType, number, content, command.EffectiveFrom!.Value, command.Note, user.UserId!, user.Name ?? user.UserId!, now),
            ct);

        return RuleSetVersionDto.From(created!, []);
    }
}
