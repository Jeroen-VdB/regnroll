using System.Security.Cryptography;
using System.Text;

namespace Regnroll.App.Services;

/// <summary>
/// AES-256-GCM for one-time payloads plus link id/key generation.
/// Link ids carry 128 bits of entropy, keys 256 bits, both base64url encoded.
/// Storage only ever sees SHA-256(id); the key exists exclusively in the generated URL.
/// </summary>
public sealed class CryptoService
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public string GenerateLinkId() => ToBase64Url(RandomNumberGenerator.GetBytes(16));

    public string GenerateKey() => ToBase64Url(RandomNumberGenerator.GetBytes(32));

    /// <summary>Lowercase hex SHA-256 of the raw link id; used as the table RowKey.</summary>
    public static string HashId(string linkId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(linkId))).ToLowerInvariant();

    public (string Nonce, string CiphertextWithTag) Encrypt(string plaintext, string keyBase64Url)
    {
        var key = FromBase64Url(keyBase64Url);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[TagSize];

        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce, plainBytes, cipher, tag);

        var combined = new byte[cipher.Length + TagSize];
        cipher.CopyTo(combined, 0);
        tag.CopyTo(combined, cipher.Length);
        return (Convert.ToBase64String(nonce), Convert.ToBase64String(combined));
    }

    /// <summary>Throws <see cref="CryptographicException"/> (tag mismatch) when the key is wrong.</summary>
    public string Decrypt(string nonceBase64, string ciphertextWithTagBase64, string keyBase64Url)
    {
        var key = FromBase64Url(keyBase64Url);
        var nonce = Convert.FromBase64String(nonceBase64);
        var combined = Convert.FromBase64String(ciphertextWithTagBase64);
        if (combined.Length < TagSize)
        {
            throw new CryptographicException("Ciphertext too short.");
        }

        var cipher = combined.AsSpan(0, combined.Length - TagSize);
        var tag = combined.AsSpan(combined.Length - TagSize);
        var plain = new byte[cipher.Length];

        using var aes = new AesGcm(key, TagSize);
        aes.Decrypt(nonce, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }

    internal static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static byte[] FromBase64Url(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch
        {
            2 => s + "==",
            3 => s + "=",
            _ => s,
        };
        return Convert.FromBase64String(s);
    }
}
