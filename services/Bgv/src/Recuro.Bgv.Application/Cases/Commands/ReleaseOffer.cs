using Recuro.Bgv.Application.Abstractions;
using Recuro.Bgv.Domain.Cases;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;

namespace Recuro.Bgv.Application.Cases.Commands;

/// <summary>
/// RCU-BGV-005 (frontend <c>releaseOfferAfterBgv</c>): succeeds only when the gate is open, otherwise 409
/// listing every check that blocks it. Clearance itself (and the one <c>bgv.cleared</c>, which moves the
/// application to Offer) happens when the last check clears.
/// </summary>
public sealed record ReleaseOfferCommand(string CaseRef) : ICommand;

internal sealed class ReleaseOfferCommandHandler(IBgvCaseRepository cases) : ICommandHandler<ReleaseOfferCommand>
{
    public async Task<Result> Handle(ReleaseOfferCommand command, CancellationToken ct)
    {
        var bgvCase = await cases.FindAsync(command.CaseRef, ct);
        return bgvCase is null ? BgvErrors.NotFound(command.CaseRef) : bgvCase.EnsureReleasable();
    }
}
