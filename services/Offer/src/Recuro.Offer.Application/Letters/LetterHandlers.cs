using System.Text.Json.Serialization;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Offer.Application.Abstractions;
using Recuro.Offer.Domain.Letters;
using Recuro.Offer.Domain.Offers;

namespace Recuro.Offer.Application.Letters;

/// <summary>
/// RCU-OFR-004: every approval issues the next letter version, generated in the same transaction as the
/// approval so an approved offer always has its letter.
/// </summary>
internal sealed class GenerateLetterOnApproval(ILetterRenderer renderer, IOfferDocumentRepository documents, TimeProvider clock)
    : IDomainEventHandler<OfferApproved>
{
    public const string PdfContentType = "application/pdf";

    public Task Handle(OfferApproved domainEvent, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        var offer = domainEvent.Offer;
        documents.Add(OfferDocument.Create(offer.Id, offer.LetterVersion, OfferDocumentKind.Letter, PdfContentType, renderer.Render(offer), clock.GetUtcNow()));
        return Task.CompletedTask;
    }
}

/// <summary>Body of the e-sign provider's callback, as the adapter normalises it.</summary>
public sealed record ESignCallback(
    Guid TenantId,
    Guid OfferId,
    int LetterVersion,
    string? EnvelopeId,
    string? Status,
    [property: JsonPropertyName("signedDocument")] string? SignedDocumentBase64);

/// <summary>
/// RCU-OFR-004: the provider reports the envelope's outcome. Signed → the countersigned copy is filed and
/// the offer accepted; declined → the offer is declined. Repeated callbacks are no-ops.
/// </summary>
public sealed record ApplyESignCallbackCommand(ESignCallback Callback) : ICommand;

internal sealed class ApplyESignCallbackCommandHandler(
    IOfferRepository offers,
    IOfferDocumentRepository documents,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICommandHandler<ApplyESignCallbackCommand>
{
    public const int MaxDocumentBytes = 10 * 1024 * 1024;

    public async Task<Result> Handle(ApplyESignCallbackCommand command, CancellationToken ct)
    {
        var callback = command.Callback;
        var offer = await offers.GetAsync(callback.OfferId, ct);
        if (offer is null)
        {
            return OfferErrors.NotFound(callback.OfferId);
        }

        if (callback.LetterVersion != offer.LetterVersion)
        {
            return Error.Conflict("letter_superseded", $"Letter v{callback.LetterVersion} was superseded by v{offer.LetterVersion}.");
        }

        var now = clock.GetUtcNow();
        Result result;
        switch (callback.Status)
        {
            case "signed":
                result = offer.Accept("Candidate (e-sign)", now);
                if (result.IsSuccess && !string.IsNullOrEmpty(callback.SignedDocumentBase64)
                    && await documents.GetLatestAsync(offer.Id, OfferDocumentKind.SignedCopy, ct) is not { } existing)
                {
                    var bytes = Convert.FromBase64String(callback.SignedDocumentBase64);
                    if (bytes.Length > MaxDocumentBytes)
                    {
                        return Error.Validation("document_too_large", "The signed document is larger than 10 MB.");
                    }

                    documents.Add(OfferDocument.Create(offer.Id, offer.LetterVersion, OfferDocumentKind.SignedCopy, GenerateLetterOnApproval.PdfContentType, bytes, now));
                }

                break;
            case "declined":
                result = offer.Decline("Declined in e-sign", "Candidate (e-sign)", now);
                break;
            default:
                return Error.Validation("status_unknown", "status is signed or declined.");
        }

        if (result.IsFailure)
        {
            return result;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
