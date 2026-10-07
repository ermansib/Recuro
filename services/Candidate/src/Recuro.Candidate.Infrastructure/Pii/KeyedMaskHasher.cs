using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Recuro.Candidate.Application.Abstractions;

namespace Recuro.Candidate.Infrastructure.Pii;

/// <summary>
/// The <c>hash</c> masking strategy: HMAC-SHA256 under the blind-index key, so equal values group
/// together but a phone number can't be recovered by hashing every possible number.
/// </summary>
internal sealed class KeyedMaskHasher(IOptions<PiiOptions> options) : IMaskHasher
{
    private const int Length = 16;
    private readonly byte[] _key = Convert.FromBase64String(options.Value.BlindIndexKey);

    public string Hash(string field, string value) =>
        Convert.ToHexStringLower(HMACSHA256.HashData(_key, Encoding.UTF8.GetBytes($"mask:{field}:{value}")))[..Length];
}
