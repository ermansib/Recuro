using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Offer.Application.Abstractions;

namespace Recuro.Offer.Application.Offers.Commands;

/// <summary>
/// RCU-OFR-006 for the current tenant: expire sent offers past their validity, and chase the rest that
/// are due (<c>offer.chase_due</c>). Returns how many offers changed.
/// </summary>
public sealed record ScanOfferLifecycleCommand : ICommand<int>
{
    public const int BatchSize = 200;
}

internal sealed class ScanOfferLifecycleCommandHandler(IOfferRepository offers, IUnitOfWork unitOfWork, TimeProvider clock)
    : ICommandHandler<ScanOfferLifecycleCommand, int>
{
    public async Task<Result<int>> Handle(ScanOfferLifecycleCommand command, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var changed = 0;
        foreach (var offer in await offers.ListLifecycleDueAsync(now, ScanOfferLifecycleCommand.BatchSize, ct))
        {
            if (offer.ExpireIfDue(now) || offer.RaiseChaseIfDue(now))
            {
                changed++;
            }
        }

        if (changed > 0)
        {
            await unitOfWork.SaveChangesAsync(ct);
        }

        return changed;
    }
}
