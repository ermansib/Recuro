using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Recuro.Candidate.Infrastructure.Pii;

namespace Recuro.Candidate.UnitTests;

public class PiiTests
{
    private static readonly string KeyA = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    private static readonly string KeyB = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static AesGcmPiiCipher Cipher(string active, params (string Id, string Key)[] keys)
    {
        var options = new PiiOptions { ActiveKeyId = active, BlindIndexKey = KeyA };
        foreach (var (id, key) in keys)
        {
            options.Keys[id] = key;
        }

        return new AesGcmPiiCipher(Options.Create(options));
    }

    [Fact]
    public void Text_round_trips_and_never_appears_in_the_ciphertext()
    {
        var cipher = Cipher("k1", ("k1", KeyA));

        var sealedValue = cipher.Encrypt("rahul.mehta@email.example");

        Assert.StartsWith("k1:", sealedValue, StringComparison.Ordinal);
        Assert.DoesNotContain("rahul", sealedValue, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("rahul.mehta@email.example", cipher.Decrypt(sealedValue));
    }

    [Fact]
    public void The_same_value_encrypts_differently_each_time()
    {
        var cipher = Cipher("k1", ("k1", KeyA));

        Assert.NotEqual(cipher.Encrypt("same"), cipher.Encrypt("same"));
    }

    [Fact]
    public void After_key_rotation_old_values_stay_readable()
    {
        var before = Cipher("k1", ("k1", KeyA)).Encrypt("old");
        var rotated = Cipher("k2", ("k1", KeyA), ("k2", KeyB));

        Assert.Equal("old", rotated.Decrypt(before));
        Assert.StartsWith("k2:", rotated.Encrypt("new"), StringComparison.Ordinal);
    }

    [Fact]
    public void Tampering_is_detected()
    {
        var cipher = Cipher("k1", ("k1", KeyA));
        var bytes = cipher.EncryptBytes("%PDF-1.7 cv"u8);
        bytes[^1] ^= 0xFF;

        Assert.ThrowsAny<CryptographicException>(() => cipher.DecryptBytes(bytes));
    }

    [Fact]
    public void Files_round_trip()
    {
        var cipher = Cipher("k1", ("k1", KeyA));

        Assert.Equal("%PDF-1.7 cv"u8.ToArray(), cipher.DecryptBytes(cipher.EncryptBytes("%PDF-1.7 cv"u8)));
    }

    [Fact]
    public void Fingerprints_match_on_normalised_contact_details_only()
    {
        var fingerprinter = new ContactFingerprinter(Options.Create(new PiiOptions { BlindIndexKey = KeyA }));

        var a = fingerprinter.Compute(" Rahul.Mehta@Email.example ", "+91 98200 40001");
        var b = fingerprinter.Compute("rahul.mehta@email.example", "098200-40001");
        var c = fingerprinter.Compute("other@email.example", "98200 40002");

        Assert.Equal(a, b);
        Assert.NotEqual(a.Email, c.Email);
        Assert.NotEqual(a.Phone, c.Phone);
        Assert.Null(fingerprinter.Compute("x@y.example", "12").Phone);
    }
}
