using FluentValidation;
using Recuro.Bgv.Application.Abstractions;
using Recuro.Bgv.Domain.Cases;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;

namespace Recuro.Bgv.Application.Cases.Commands;

/// <summary>RCU-VND-003: moves every open case of a (de-empanelled) vendor to another active BGV agency.</summary>
public sealed record ReassignVendorCommand(string FromVendorId, string ToVendorId) : ICommand<ReassignmentResultDto>;

public sealed record ReassignmentResultDto(string FromVendorId, string ToVendorId, string ToVendor, IReadOnlyList<string> AppIds);

internal sealed class ReassignVendorCommandValidator : AbstractValidator<ReassignVendorCommand>
{
    public ReassignVendorCommandValidator()
    {
        RuleFor(c => c.FromVendorId).NotEmpty().MaximumLength(BgvLimits.VendorIdLength);
        RuleFor(c => c.ToVendorId).NotEmpty().MaximumLength(BgvLimits.VendorIdLength).NotEqual(c => c.FromVendorId);
    }
}

internal sealed class ReassignVendorCommandHandler(
    IBgvCaseRepository cases,
    IVendorDirectory vendors,
    IUnitOfWork unitOfWork) : ICommandHandler<ReassignVendorCommand, ReassignmentResultDto>
{
    public async Task<Result<ReassignmentResultDto>> Handle(ReassignVendorCommand command, CancellationToken ct)
    {
        var target = await vendors.GetAsync(command.ToVendorId, ct);
        if (target is null || !target.IsActiveBgvAgency)
        {
            return BgvErrors.VendorNotActive(command.ToVendorId);
        }

        var moved = new List<string>();
        foreach (var bgvCase in await cases.ListOpenByVendorAsync(command.FromVendorId, ct))
        {
            if (bgvCase.Reassign(new VendorRef(target.VendorId, target.Name)).IsSuccess)
            {
                moved.Add(bgvCase.AppId);
            }
        }

        await unitOfWork.SaveChangesAsync(ct);
        return new ReassignmentResultDto(command.FromVendorId, target.VendorId, target.Name, moved);
    }
}
