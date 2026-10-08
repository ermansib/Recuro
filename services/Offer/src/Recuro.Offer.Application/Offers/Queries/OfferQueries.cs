using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Offer.Application.Abstractions;
using Recuro.Offer.Domain.Letters;
using Recuro.Offer.Domain.Offers;

namespace Recuro.Offer.Application.Offers.Queries;

/// <summary>The frontend's listOffers: newest first, masked for the caller. Optional state and application filters.</summary>
public sealed record ListOffersQuery(OfferState? State, string? AppId, int Limit = ListOffersQuery.DefaultLimit) : IQuery<IReadOnlyList<OfferDto>>
{
    public const int DefaultLimit = 100;
    public const int MaxLimit = 500;
}

internal sealed class ListOffersQueryHandler(IOfferRepository offers, ICallerMask mask) : IQueryHandler<ListOffersQuery, IReadOnlyList<OfferDto>>
{
    public async Task<Result<IReadOnlyList<OfferDto>>> Handle(ListOffersQuery query, CancellationToken ct)
    {
        var rows = string.IsNullOrWhiteSpace(query.AppId)
            ? await offers.ListAsync(query.State, Math.Clamp(query.Limit, 1, ListOffersQuery.MaxLimit), ct)
            : (await offers.ListForApplicationAsync(query.AppId.Trim(), ct)).Where(o => query.State is null || o.State == query.State).ToList();
        var apply = await mask.ForCallerAsync(ct);
        return rows.Select(o => apply(OfferDto.From(o))).ToList();
    }
}

public sealed record GetOfferQuery(Guid OfferId) : IQuery<VersionedOffer>;

internal sealed class GetOfferQueryHandler(OfferWriter writer) : IQueryHandler<GetOfferQuery, VersionedOffer>
{
    public async Task<Result<VersionedOffer>> Handle(GetOfferQuery query, CancellationToken ct)
    {
        var loaded = await writer.LoadAsync(query.OfferId, null, ct);
        return loaded.IsFailure ? loaded.Error! : await writer.ViewAsync(loaded.Value, ct);
    }
}

/// <summary>A stored document to download.</summary>
public sealed record OfferFile(byte[] Content, string ContentType, string FileName);

/// <summary>RCU-OFR-004: the latest letter (or the signed copy) for HR staff.</summary>
public sealed record GetOfferLetterQuery(Guid OfferId, bool SignedCopy) : IQuery<OfferFile>;

internal sealed class GetOfferLetterQueryHandler(IOfferRepository offers, IOfferDocumentRepository documents) : IQueryHandler<GetOfferLetterQuery, OfferFile>
{
    public async Task<Result<OfferFile>> Handle(GetOfferLetterQuery query, CancellationToken ct)
    {
        if (await offers.GetAsync(query.OfferId, ct) is null)
        {
            return OfferErrors.NotFound(query.OfferId);
        }

        var kind = query.SignedCopy ? OfferDocumentKind.SignedCopy : OfferDocumentKind.Letter;
        var document = await documents.GetLatestAsync(query.OfferId, kind, ct);
        return document is null
            ? Error.NotFound("letter_not_found", query.SignedCopy ? "No signed copy has been filed yet." : "No letter has been generated yet.")
            : LetterFiles.From(document);
    }
}

/// <summary>RCU-OFR-004: a short-lived presigned link to the current letter, e.g. to share with the candidate.</summary>
public sealed record CreateLetterLinkCommand(Guid OfferId) : ICommand<LetterLink>
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromDays(7);
}

internal sealed class CreateLetterLinkCommandHandler(IOfferRepository offers, ILetterLinks links, ITenantContext tenant, TimeProvider clock)
    : ICommandHandler<CreateLetterLinkCommand, LetterLink>
{
    public async Task<Result<LetterLink>> Handle(CreateLetterLinkCommand command, CancellationToken ct)
    {
        var offer = await offers.GetAsync(command.OfferId, ct);
        if (offer is null)
        {
            return OfferErrors.NotFound(command.OfferId);
        }

        if (offer.LetterVersion == 0)
        {
            return Error.NotFound("letter_not_found", "No letter has been generated yet.");
        }

        return links.Create(tenant.RequiredTenantId, offer.Id, offer.LetterVersion, clock.GetUtcNow().Add(CreateLetterLinkCommand.Lifetime));
    }
}

/// <summary>Opens a presigned link: no session, the signature is the credential. Runs in the link's tenant scope.</summary>
public sealed record GetLetterByLinkQuery(Guid OfferId, int LetterVersion) : IQuery<OfferFile>;

internal sealed class GetLetterByLinkQueryHandler(IOfferDocumentRepository documents) : IQueryHandler<GetLetterByLinkQuery, OfferFile>
{
    public async Task<Result<OfferFile>> Handle(GetLetterByLinkQuery query, CancellationToken ct)
    {
        var document = await documents.GetLatestAsync(query.OfferId, OfferDocumentKind.Letter, ct);

        // Only the version the link was issued for: a superseded letter's link stops working.
        return document is null || document.LetterVersion != query.LetterVersion
            ? Error.NotFound("letter_not_found", "This link is no longer valid.")
            : LetterFiles.From(document);
    }
}

internal static class LetterFiles
{
    public static OfferFile From(OfferDocument document) => new(
        document.Content,
        document.ContentType,
        $"offer-{document.OfferId:N}-v{document.LetterVersion}{(document.Kind == OfferDocumentKind.SignedCopy ? "-signed" : string.Empty)}.pdf");
}
