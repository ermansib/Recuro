using System.Globalization;
using System.Text.Json.Serialization;
using Recuro.Offer.Domain.Offers;

namespace Recuro.Offer.Application.Offers;

public sealed record ComponentsDto(decimal Fixed, decimal Variable, decimal Benefits);

public sealed record BandDto(decimal Min, decimal Max);

public sealed record TrailDto(
    string Title,
    string Detail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? Approved);

public sealed record RouteDto(decimal? Total, decimal? Deviation, bool WithinBand, string ApproverRole, string Label, string ConfigVersionId);

/// <summary>
/// The frontend's <c>Offer</c>, field for field, plus lifecycle fields the screen can show. Masked
/// fields (RCU-AUT-004) are null; amounts in the trail are blanked with them.
/// </summary>
public sealed record OfferDto(
    string Id,
    string AppId,
    string ReqId,
    string CandidateName,
    string Designation,
    string Grade,
    string Location,
    string ReportingManager,
    string JoiningDate,
    int ProbationMonths,
    ComponentsDto? Components,
    BandDto? Band,
    OfferState State,
    int LetterVersion,
    IReadOnlyList<TrailDto> Trail,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RouteDto? Route,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SentAt,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ExpiresAt,
    bool Conditional)
{
    public static OfferDto From(JobOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        return new OfferDto(
            offer.Id.ToString(),
            offer.AppId,
            offer.ReqId,
            offer.CandidateName,
            offer.Designation,
            offer.Grade,
            offer.Location,
            offer.ReportingManager,
            offer.JoiningDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            offer.ProbationMonths,
            new ComponentsDto(offer.Components.Fixed, offer.Components.Variable, offer.Components.Benefits),
            new BandDto(offer.Band.Min, offer.Band.Max),
            offer.State,
            offer.LetterVersion,
            offer.Trail.Select(t => new TrailDto(t.Title, $"{t.Detail} · {Iso(t.At)}", t.Approved)).ToList(),
            offer.Route is { } r ? new RouteDto(r.Total, r.Deviation, r.WithinBand, r.ApproverRole, r.Label, r.ConfigVersionId) : null,
            offer.SentAt is { } sent ? Iso(sent) : null,
            offer.ExpiresAt is { } expires ? Iso(expires) : null,
            offer.Conditional);
    }

    public static string Iso(DateTimeOffset value) => value.ToString("yyyy-MM-dd'T'HH:mm:ssK", CultureInfo.InvariantCulture);
}

/// <summary>An offer with its concurrency version, which the API returns as the ETag.</summary>
public sealed record VersionedOffer(OfferDto Offer, uint Version);
