using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Recuro.Candidate.Application.Abstractions;
using Recuro.Candidate.Domain.Candidates;

namespace Recuro.Candidate.Infrastructure.Pii;

/// <summary>
/// HMAC-SHA256 blind indexes of normalised contact details: equal inputs give equal fingerprints, but a
/// fingerprint reveals nothing without the key. Emails are trimmed and lower-cased; phones keep their last
/// ten digits, so <c>+91 98200 40001</c> and <c>098200-40001</c> match.
/// </summary>
internal sealed class ContactFingerprinter(IOptions<PiiOptions> options) : IContactFingerprinter
{
    private const int PhoneDigits = 10;
    private readonly byte[] _key = Convert.FromBase64String(options.Value.BlindIndexKey);

    public ContactFingerprints Compute(string email, string? phone)
    {
        ArgumentNullException.ThrowIfNull(email);
        var digits = new string((phone ?? string.Empty).Where(char.IsAsciiDigit).ToArray());
        var normalisedPhone = digits.Length >= 7 ? digits[^Math.Min(PhoneDigits, digits.Length)..] : null;
        return new ContactFingerprints(
            Hash("email", email.Trim().ToLowerInvariant()),
            normalisedPhone is null ? null : Hash("phone", normalisedPhone));
    }

    private string Hash(string kind, string value) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"{kind}:{value}")));
}
