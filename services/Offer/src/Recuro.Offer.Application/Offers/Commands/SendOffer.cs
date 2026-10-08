using Microsoft.Extensions.Logging;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Offer.Application.Abstractions;
using Recuro.Offer.Domain.Bgv;
using Recuro.Offer.Domain.Letters;
using Recuro.Offer.Domain.Offers;

namespace Recuro.Offer.Application.Offers.Commands;

/// <summary>Body of <c>POST /api/v1/offers/{id}/send</c>. <c>conditional</c> is BGV-009's conditional-offer mode (HR Head).</summary>
public sealed record SendOfferRequest(bool? Conditional);

/// <summary>
/// RCU-OFR-004/006/007 (frontend sendOffer): release an approved offer. Blocked (409, listing blockers)
/// until BGV is cleared, except in conditional-offer mode, which HR Head may use while no adverse
/// finding is open. The letter goes out through the e-sign adapter; the chase and expiry dates are worked
/// out now with the business calendar, so the lifecycle job needs no calls.
/// </summary>
public sealed record SendOfferCommand(Guid OfferId, uint? ExpectedVersion, SendOfferRequest Request) : ICommand<VersionedOffer>;

internal sealed partial class SendOfferCommandHandler(
    OfferWriter writer,
    IBgvTrackRepository bgv,
    IOfferDocumentRepository documents,
    IOfferRulesSource rulesSource,
    IWorkingDays workingDays,
    ILetterLinks links,
    IESignGateway eSign,
    ITenantContext tenant,
    ICurrentUser user,
    IUnitOfWork unitOfWork,
    ILogger<SendOfferCommandHandler> logger) : ICommandHandler<SendOfferCommand, VersionedOffer>
{
    public async Task<Result<VersionedOffer>> Handle(SendOfferCommand command, CancellationToken ct)
    {
        var loaded = await writer.LoadAsync(command.OfferId, command.ExpectedVersion, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error!;
        }

        var offer = loaded.Value;
        if (offer.State != OfferState.Approved)
        {
            return Error.Conflict("offer_not_approved", "Offer must be approved before release.");
        }

        var conditional = command.Request.Conditional == true;
        var track = await bgv.GetAsync(offer.AppId, ct);
        var blockers = BgvTrack.BlockersFor(track);
        if (conditional)
        {
            if (!user.IsInRole("hrhead"))
            {
                return Error.Forbidden("conditional_offer_hr_head", "Only HR Head can release a conditional offer before BGV clears.");
            }

            if (track is null || track.Gate == BgvGate.Adverse)
            {
                return OfferErrors.ReleaseBlocked(blockers);
            }
        }
        else if (blockers.Count > 0)
        {
            return OfferErrors.ReleaseBlocked(blockers);
        }

        var letter = await documents.GetLatestAsync(offer.Id, OfferDocumentKind.Letter, ct);
        if (letter is null || letter.LetterVersion != offer.LetterVersion)
        {
            return Error.Conflict("letter_missing", $"Offer letter v{offer.LetterVersion} has not been generated.");
        }

        var rules = await rulesSource.GetAsync(ct);
        var now = writer.Now;
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var expiresOn = await workingDays.AddAsync(today, rules.ValidityWorkingDays, ct);
        var firstChaseOn = await workingDays.AddAsync(today, rules.FirstChaseAfterWorkingDays, ct);
        var plan = new SendPlan(
            new DateTimeOffset(firstChaseOn.ToDateTime(TimeOnly.FromTimeSpan(now.UtcDateTime.TimeOfDay)), TimeSpan.Zero),
            new DateTimeOffset(expiresOn.ToDateTime(new TimeOnly(23, 59, 59)), TimeSpan.Zero),
            TimeSpan.FromDays(rules.ChaseEveryDays),
            conditional);

        var sent = offer.Send(plan, writer.Actor, now);
        if (sent.IsFailure)
        {
            return sent.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);

        // The offer is released; the envelope is best-effort and idempotent on offer + letter version.
        var link = links.Create(tenant.RequiredTenantId, offer.Id, offer.LetterVersion, plan.ExpiresAt);
        try
        {
            await eSign.SendAsync(new ESignEnvelope(offer.Id, offer.LetterVersion, offer.CandidateId, link.Url, plan.ExpiresAt), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            EnvelopeFailed(logger, offer.Id, ex);
        }

        return await writer.ViewAsync(offer, ct);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Offer {OfferId} was released but the e-sign envelope failed; resend it from the e-sign provider")]
    private static partial void EnvelopeFailed(ILogger logger, Guid offerId, Exception ex);
}
