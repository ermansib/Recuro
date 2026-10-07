using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Recuro.Candidate.Infrastructure.Pii;

/// <summary>Encrypts personal data before it reaches the database or the file store.</summary>
public interface IPiiCipher
{
    string Encrypt(string plaintext);

    string Decrypt(string sealedValue);

    byte[] EncryptBytes(ReadOnlySpan<byte> plaintext);

    byte[] DecryptBytes(ReadOnlySpan<byte> sealedValue);
}

/// <summary>
/// AES-256-GCM (authenticated) with a fresh 96-bit nonce per value. Text values are stored as
/// <c>{keyId}:{base64(nonce|tag|ciphertext)}</c>; byte values carry the key id in a length-prefixed header.
/// </summary>
internal sealed class AesGcmPiiCipher : IPiiCipher
{
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private readonly Dictionary<string, byte[]> _keys;
    private readonly string _activeKeyId;

    public AesGcmPiiCipher(IOptions<PiiOptions> options)
    {
        var value = options.Value;
        _keys = value.Keys.ToDictionary(k => k.Key, k => Convert.FromBase64String(k.Value), StringComparer.Ordinal);
        _activeKeyId = value.ActiveKeyId;
        if (!_keys.ContainsKey(_activeKeyId))
        {
            throw new InvalidOperationException($"Pii:Keys has no key named '{_activeKeyId}'.");
        }

        if (_keys.Keys.Any(k => k.Contains(':', StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Pii key ids cannot contain ':'.");
        }
    }

    public string Encrypt(string plaintext)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        var sealedBytes = Seal(_keys[_activeKeyId], Encoding.UTF8.GetBytes(plaintext));
        return string.Create(CultureInfo.InvariantCulture, $"{_activeKeyId}:{Convert.ToBase64String(sealedBytes)}");
    }

    public string Decrypt(string sealedValue)
    {
        ArgumentNullException.ThrowIfNull(sealedValue);
        var separator = sealedValue.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
        {
            throw new CryptographicException("Value is not an encrypted Recuro field.");
        }

        var key = KeyFor(sealedValue[..separator]);
        return Encoding.UTF8.GetString(Open(key, Convert.FromBase64String(sealedValue[(separator + 1)..])));
    }

    public byte[] EncryptBytes(ReadOnlySpan<byte> plaintext)
    {
        var keyId = Encoding.UTF8.GetBytes(_activeKeyId);
        var sealedBytes = Seal(_keys[_activeKeyId], plaintext);
        var output = new byte[1 + keyId.Length + sealedBytes.Length];
        output[0] = checked((byte)keyId.Length);
        keyId.CopyTo(output, 1);
        sealedBytes.CopyTo(output, 1 + keyId.Length);
        return output;
    }

    public byte[] DecryptBytes(ReadOnlySpan<byte> sealedValue)
    {
        if (sealedValue.Length < 1 || sealedValue.Length < 1 + sealedValue[0])
        {
            throw new CryptographicException("Value is not an encrypted Recuro file.");
        }

        var keyIdLength = sealedValue[0];
        var key = KeyFor(Encoding.UTF8.GetString(sealedValue.Slice(1, keyIdLength)));
        return Open(key, sealedValue[(1 + keyIdLength)..]);
    }

    private byte[] KeyFor(string keyId) =>
        _keys.TryGetValue(keyId, out var key)
            ? key
            : throw new CryptographicException($"Encryption key '{keyId}' is not configured.");

    private static byte[] Seal(byte[] key, ReadOnlySpan<byte> plaintext)
    {
        var output = new byte[NonceSize + TagSize + plaintext.Length];
        var nonce = output.AsSpan(0, NonceSize);
        var tag = output.AsSpan(NonceSize, TagSize);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plaintext, output.AsSpan(NonceSize + TagSize), tag);
        return output;
    }

    private static byte[] Open(byte[] key, ReadOnlySpan<byte> sealedBytes)
    {
        if (sealedBytes.Length < NonceSize + TagSize)
        {
            throw new CryptographicException("Encrypted value is truncated.");
        }

        var plaintext = new byte[sealedBytes.Length - NonceSize - TagSize];
        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(sealedBytes[..NonceSize], sealedBytes[(NonceSize + TagSize)..], sealedBytes.Slice(NonceSize, TagSize), plaintext);
        return plaintext;
    }
}
