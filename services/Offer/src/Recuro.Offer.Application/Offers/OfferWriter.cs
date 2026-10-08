using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Domain;
using Recuro.Offer.Application.Abstractions;
using Recuro.Offer.Domain.Offers;

namespace Recuro.Offer.Application.Offers;

/// <summary>Loads an offer, checks If-Match, applies a change, saves and returns it masked for the caller.</summary>
internal sealed class OfferWriter(IOfferRepository offers, IUnitOfWork unitOfWork, ICallerMask mask, ICurrentUser user, TimeProvider clock)
{
    public string Actor => user.Name ?? user.UserId ?? string.Empty;

    public DateTimeOffset Now => clock.GetUtcNow();

    public async Task<Result<JobOffer>> LoadAsync(Guid id, uint? expectedVersion, CancellationToken ct)
    {
        var offer = await offers.GetAsync(id, ct);
        if (offer is null)
        {
            return OfferErrors.NotFound(id);
        }

        if (expectedVersion is { } expected && expected != offer.Version)
        {
            return OfferErrors.StaleVersion(id);
        }

        return offer;
    }

    public async Task<Result<VersionedOffer>> WriteAsync(Guid id, uint? expectedVersion, Func<JobOffer, Result> change, CancellationToken ct)
    {
        var loaded = await LoadAsync(id, expectedVersion, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var changed = change(loaded.Value);
        if (changed.IsFailure)
        {
            return changed.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return await ViewAsync(loaded.Value, ct);
    }

    public async Task<VersionedOffer> ViewAsync(JobOffer offer, CancellationToken ct)
    {
        var apply = await mask.ForCallerAsync(ct);
        return new VersionedOffer(apply(OfferDto.From(offer)), offer.Version);
    }
}
