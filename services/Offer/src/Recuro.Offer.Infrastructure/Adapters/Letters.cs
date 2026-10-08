using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Infrastructure.Documents;
using Recuro.Offer.Application.Abstractions;
using Recuro.Offer.Domain.Offers;

namespace Recuro.Offer.Infrastructure.Adapters;

public sealed class LetterOptions
{
    public const string SectionName = "Letters";

    /// <summary>Public base address the presigned links point at (the gateway), e.g. <c>http://localhost:5000/</c>.</summary>
    public Uri PublicBaseUrl { get; set; } = new("http://localhost:5000/");

    /// <summary>HMAC key for presigned letter links. Set a long random secret outside Development.</summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>Shared secret the e-sign provider signs its callbacks with (HMAC-SHA256 of the body).</summary>
    public string CallbackSecret { get; set; } = string.Empty;
}

/// <summary>
/// RCU-OFR-004: the offer letter as a PDF. Content comes from the offer; the issuing company's name and
/// branding are the tenant's (white-label), so the letter names none.
/// </summary>
internal sealed class PdfLetterRenderer : ILetterRenderer
{
    public byte[] Render(JobOffer offer)
    {
        ArgumentNullException.ThrowIfNull(offer);
        var c = offer.Components;
        var lines = new List<string>
        {
            Format($"Letter version {offer.LetterVersion} · Offer {offer.Id} · Application {offer.AppId} · Requisition {offer.ReqId}"),
            string.Empty,
            $"Dear {offer.CandidateName},",
            string.Empty,
            Format($"We are pleased to offer you the position of {offer.Designation} (Grade {offer.Grade}) at {offer.Location}, reporting to {offer.ReportingManager}."),
            string.Empty,
            "Compensation (annual, cost to company):",
            Format($"    Fixed pay:   ₹{c.Fixed:0.0} lakh"),
            Format($"    Variable pay:   ₹{c.Variable:0.0} lakh"),
            Format($"    Benefits:   ₹{c.Benefits:0.0} lakh"),
            Format($"    Total CTC:   ₹{c.Total:0.0} lakh"),
            string.Empty,
            Format($"Joining date: {offer.JoiningDate:dd MMM yyyy}. Probation: {offer.ProbationMonths} months."),
            string.Empty,
            "Background verification: this offer is subject to satisfactory background verification. By accepting it you consent to the checks described in the attached consent form.",
            "Conflict of interest: please complete and return the conflict-of-interest declaration (COI) attached to this letter.",
            string.Empty,
            "Please accept this offer by e-signing it before it expires.",
            string.Empty,
            "Talent Acquisition",
        };
        return SimplePdf.Render("Offer of Employment", lines);
    }

    private static string Format(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}

/// <summary>
/// RCU-OFR-004 presigned links: <c>…/api/v1/offers/letters/{tenant}/{offer}/{version}?exp=…&amp;sig=…</c>,
/// where <c>sig</c> is an HMAC-SHA256 over the path and expiry. Valid only until <c>exp</c>.
/// </summary>
internal sealed class HmacLetterLinks(IOptions<LetterOptions> options) : ILetterLinks
{
    public LetterLink Create(Guid tenantId, Guid offerId, int letterVersion, DateTimeOffset expiresAt)
    {
        var expires = expiresAt.ToUnixTimeSeconds();
        var path = Path(tenantId, offerId, letterVersion);
        var url = new Uri(options.Value.PublicBaseUrl, string.Create(CultureInfo.InvariantCulture, $"{path}?exp={expires}&sig={Sign(path, expires)}"));
        return new LetterLink(url.ToString(), DateTimeOffset.FromUnixTimeSeconds(expires));
    }

    public bool Verify(Guid tenantId, Guid offerId, int letterVersion, long expiresUnix, string signature, DateTimeOffset now)
    {
        if (string.IsNullOrEmpty(signature) || now.ToUnixTimeSeconds() > expiresUnix)
        {
            return false;
        }

        var expected = Encoding.ASCII.GetBytes(Sign(Path(tenantId, offerId, letterVersion), expiresUnix));
        return CryptographicOperations.FixedTimeEquals(expected, Encoding.ASCII.GetBytes(signature));
    }

    internal static string Path(Guid tenantId, Guid offerId, int letterVersion) =>
        string.Create(CultureInfo.InvariantCulture, $"api/v1/offers/letters/{tenantId:D}/{offerId:D}/{letterVersion}");

    private string Sign(string path, long expires)
    {
        var key = options.Value.SigningKey;
        if (string.IsNullOrEmpty(key))
        {
            throw new InvalidOperationException("Letters:SigningKey is not set.");
        }

        var mac = HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{path}\n{expires}")));
        return Convert.ToBase64String(mac).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

/// <summary>Verifies the e-sign provider's callback signature (hex HMAC-SHA256 of the raw body).</summary>
public sealed class ESignCallbackVerifier(IOptions<LetterOptions> options)
{
    public bool Verify(byte[] body, string? signature)
    {
        ArgumentNullException.ThrowIfNull(body);
        var secret = options.Value.CallbackSecret;
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signature))
        {
            return false;
        }

        var expected = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body));
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant()));
    }
}

/// <summary>
/// Default e-sign adapter: logs the envelope (ids only) and returns a deterministic envelope id. A tenant's
/// e-sign provider plugs in behind <see cref="IESignGateway"/> and calls back on the signed copy.
/// </summary>
internal sealed partial class LoggingESignGateway(ILogger<LoggingESignGateway> logger) : IESignGateway
{
    public Task<string> SendAsync(ESignEnvelope envelope, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var envelopeId = string.Create(CultureInfo.InvariantCulture, $"env-{envelope.OfferId:N}-v{envelope.LetterVersion}");
        Sent(logger, envelopeId, envelope.OfferId, envelope.ExpiresAt);
        return Task.FromResult(envelopeId);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "E-sign envelope {EnvelopeId} for offer {OfferId}, valid until {ExpiresAt}")]
    private static partial void Sent(ILogger logger, string envelopeId, Guid offerId, DateTimeOffset expiresAt);
}
