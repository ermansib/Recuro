namespace Recuro.Candidate.Infrastructure.Pii;

/// <summary>
/// Field-level encryption keys for candidate personal data (BNFR-3), bound from <c>Pii</c>. Keys are
/// 32-byte AES keys in base64. Production keys come from environment variables or a free vault
/// (Infisical, OpenBao), never from a committed file. Rotate by adding a key and switching
/// <see cref="ActiveKeyId"/>: old values stay readable because each value names the key that sealed it.
/// </summary>
public sealed class PiiOptions
{
    public const string SectionName = "Pii";

    public string ActiveKeyId { get; set; } = string.Empty;

    public Dictionary<string, string> Keys { get; set; } = [];

    /// <summary>HMAC key for the email/phone blind indexes (duplicate detection). Changing it needs a re-index.</summary>
    public string BlindIndexKey { get; set; } = string.Empty;

    internal static bool IsKey(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            return false;
        }

        Span<byte> buffer = stackalloc byte[64];
        return Convert.TryFromBase64String(base64, buffer, out var written) && written == 32;
    }
}
