using FluentValidation;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Offer.Application.Abstractions;
using Recuro.Offer.Domain.Offers;

namespace Recuro.Offer.Application.Offers.Commands;

/// <summary>Body of <c>POST /api/v1/offers</c>. Designation, location and manager default to the requisition's.</summary>
public sealed record CreateOfferRequest(
    string? AppId,
    string? CandidateName,
    string? Designation,
    string? Location,
    string? ReportingManager,
    DateOnly? JoiningDate,
    int? ProbationMonths,
    ComponentsDto? Components,
    BandDto? Band);

/// <summary>
/// RCU-OFR-001: HR-TA drafts an offer for a selected application. The grade comes from the requisition
/// (it decides the approval route), and the CTC must pass the tenant's Finance rule-set.
/// </summary>
public sealed record CreateOfferCommand(CreateOfferRequest Request) : ICommand<VersionedOffer>;

internal sealed class CreateOfferCommandValidator : AbstractValidator<CreateOfferCommand>
{
    public CreateOfferCommandValidator()
    {
        RuleFor(c => c.Request).NotNull();
        RuleFor(c => c.Request.AppId).NotEmpty().MaximumLength(OfferLimits.IdLength).OverridePropertyName("appId");
        RuleFor(c => c.Request.CandidateName).NotEmpty().MaximumLength(OfferLimits.ShortText).OverridePropertyName("candidateName");
        RuleFor(c => c.Request.Designation).MaximumLength(OfferLimits.ShortText).OverridePropertyName("designation");
        RuleFor(c => c.Request.Location).MaximumLength(OfferLimits.ShortText).OverridePropertyName("location");
        RuleFor(c => c.Request.ReportingManager).MaximumLength(OfferLimits.ShortText).OverridePropertyName("reportingManager");
        RuleFor(c => c.Request.JoiningDate).NotNull().OverridePropertyName("joiningDate");
        RuleFor(c => c.Request.ProbationMonths).NotNull().InclusiveBetween(0, OfferLimits.MaxProbationMonths).OverridePropertyName("probationMonths");
        RuleFor(c => c.Request.Components).NotNull().SetValidator(new ComponentsValidator()!).OverridePropertyName("components");
        RuleFor(c => c.Request.Band).NotNull().OverridePropertyName("band");
        RuleFor(c => c.Request.Band!.Min).GreaterThanOrEqualTo(0).LessThanOrEqualTo(c => c.Request.Band!.Max).When(c => c.Request.Band is not null).OverridePropertyName("band.min");
        RuleFor(c => c.Request.Band!.Max).GreaterThan(0).LessThan(OfferLimits.MaxAmount).When(c => c.Request.Band is not null).OverridePropertyName("band.max");
    }
}

internal sealed class ComponentsValidator : AbstractValidator<ComponentsDto>
{
    public ComponentsValidator()
    {
        RuleFor(c => c.Fixed).LessThan(OfferLimits.MaxAmount).PrecisionScale(9, 1, true);
        RuleFor(c => c.Variable).LessThan(OfferLimits.MaxAmount).PrecisionScale(9, 1, true);
        RuleFor(c => c.Benefits).LessThan(OfferLimits.MaxAmount).PrecisionScale(9, 1, true);
    }
}

internal sealed class CreateOfferCommandHandler(
    IApplicationTrackRepository tracks,
    IOfferRepository offers,
    IRequisitions requisitions,
    IOfferRulesSource rulesSource,
    OfferWriter writer,
    IUnitOfWork unitOfWork) : ICommandHandler<CreateOfferCommand, VersionedOffer>
{
    public async Task<Result<VersionedOffer>> Handle(CreateOfferCommand command, CancellationToken ct)
    {
        var request = command.Request;
        var appId = request.AppId!.Trim();
        var track = await tracks.GetAsync(appId, ct);
        if (track is null || !track.OfferAllowed)
        {
            return OfferErrors.NotInOfferStage(appId, track?.Stage ?? "unknown");
        }

        if ((await offers.ListForApplicationAsync(appId, ct)).Any(o => o.IsOpen))
        {
            return OfferErrors.AlreadyExists(appId);
        }

        var requisition = await requisitions.GetAsync(track.ReqId, ct);
        if (requisition is null)
        {
            return Error.Conflict("requisition_not_found", $"Requisition {track.ReqId} for {appId} was not found.");
        }

        var components = request.Components!.ToDomain();
        var band = new PayBand(request.Band!.Min, request.Band.Max);
        var rules = await rulesSource.GetAsync(ct);
        var violations = rules.CtcRules.Validate(components);
        if (violations.Count > 0)
        {
            return OfferErrors.CtcRulesBroken(violations);
        }

        if (rules.Route(requisition.Grade, components, band) is null)
        {
            return OfferErrors.NoApprovalRule(requisition.Grade);
        }

        var offer = JobOffer.Create(
            new OfferDraft(
                appId,
                track.ReqId,
                track.CandidateId,
                request.CandidateName!.Trim(),
                Pick(request.Designation, requisition.Designation),
                requisition.Grade,
                Pick(request.Location, requisition.Location),
                Pick(request.ReportingManager, requisition.ReportingManager),
                request.JoiningDate!.Value,
                request.ProbationMonths!.Value,
                components,
                band),
            writer.Actor,
            writer.Now);
        offers.Add(offer);
        await unitOfWork.SaveChangesAsync(ct);
        return await writer.ViewAsync(offer, ct);
    }

    private static string Pick(string? requested, string fallback) => string.IsNullOrWhiteSpace(requested) ? fallback : requested.Trim();
}

internal static class ComponentsMapping
{
    public static CtcComponents ToDomain(this ComponentsDto dto) => new(dto.Fixed, dto.Variable, dto.Benefits);
}
