using System.Security.Cryptography;
using Regnroll.App.Services;
using Xunit;

namespace Regnroll.App.Tests;

public class CryptoServiceTests
{
    private readonly CryptoService _crypto = new();

    [Fact]
    public void Encrypt_Decrypt_RoundTrips()
    {
        var key = _crypto.GenerateKey();
        var (nonce, ciphertext) = _crypto.Encrypt("s3cr3t-value~!", key);

        Assert.Equal("s3cr3t-value~!", _crypto.Decrypt(nonce, ciphertext, key));
    }

    [Fact]
    public void Decrypt_WithWrongKey_ThrowsCryptographicException()
    {
        var key = _crypto.GenerateKey();
        var wrongKey = _crypto.GenerateKey();
        var (nonce, ciphertext) = _crypto.Encrypt("payload", key);

        Assert.ThrowsAny<CryptographicException>(() => _crypto.Decrypt(nonce, ciphertext, wrongKey));
    }

    [Fact]
    public void Ciphertext_DiffersFromPlaintext_AndContainsTag()
    {
        var key = _crypto.GenerateKey();
        var (_, ciphertext) = _crypto.Encrypt("payload", key);
        var bytes = Convert.FromBase64String(ciphertext);

        Assert.DoesNotContain("payload", ciphertext);
        Assert.Equal("payload".Length + 16, bytes.Length);
    }

    [Fact]
    public void GenerateLinkId_Has128BitsEntropy_Base64Url()
    {
        var id = _crypto.GenerateLinkId();

        Assert.Equal(16, CryptoService.FromBase64Url(id).Length);
        Assert.DoesNotContain('+', id);
        Assert.DoesNotContain('/', id);
        Assert.DoesNotContain('=', id);
    }

    [Fact]
    public void GenerateKey_Has256BitsEntropy()
    {
        Assert.Equal(32, CryptoService.FromBase64Url(_crypto.GenerateKey()).Length);
    }

    [Fact]
    public void GeneratedIds_AreUnique()
    {
        var ids = Enumerable.Range(0, 1000).Select(_ => _crypto.GenerateLinkId()).ToHashSet();
        Assert.Equal(1000, ids.Count);
    }

    [Fact]
    public void HashId_IsStable_LowercaseHex()
    {
        var hash = CryptoService.HashId("abc");

        Assert.Equal(CryptoService.HashId("abc"), hash);
        Assert.NotEqual(CryptoService.HashId("abd"), hash);
        Assert.Equal(64, hash.Length);
        Assert.Equal(hash, hash.ToLowerInvariant());
    }

    [Fact]
    public void Base64Url_RoundTrips_AllPaddingLengths()
    {
        foreach (var len in new[] { 15, 16, 17, 31, 32, 33 })
        {
            var bytes = RandomNumberGenerator.GetBytes(len);
            Assert.Equal(bytes, CryptoService.FromBase64Url(CryptoService.ToBase64Url(bytes)));
        }
    }
}
