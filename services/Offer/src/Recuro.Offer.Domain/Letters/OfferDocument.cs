using Recuro.BuildingBlocks.Domain;

namespace Recuro.Offer.Domain.Letters;

public enum OfferDocumentKind
{
    /// <summary>The letter generated on approval (RCU-OFR-004).</summary>
    Letter,

    /// <summary>The countersigned copy filed from the e-sign callback.</summary>
    SignedCopy,
}

/// <summary>A stored offer document. Immutable: a revised letter is a new version.</summary>
public sealed class OfferDocument : Entity, ITenantOwned
{
    private OfferDocument()
    {
    }

    public Guid TenantId { get; private set; }

    public Guid OfferId { get; private set; }

    public int LetterVersion { get; private set; }

    public OfferDocumentKind Kind { get; private set; }

    public string ContentType { get; private set; } = string.Empty;

    public byte[] Content { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public static OfferDocument Create(Guid offerId, int letterVersion, OfferDocumentKind kind, string contentType, byte[] content, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        OfferId = offerId,
        LetterVersion = letterVersion,
        Kind = kind,
        ContentType = contentType,
        Content = content,
        CreatedAt = at,
    };
}
