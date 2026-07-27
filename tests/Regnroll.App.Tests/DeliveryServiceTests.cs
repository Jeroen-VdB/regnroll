using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Regnroll.App.Models;
using Regnroll.App.Options;
using Regnroll.App.Services;
using Xunit;

namespace Regnroll.App.Tests;

public class DeliveryServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FixedTimeProvider _clock = new(T0);
    private readonly FakeGraphAppService _graph = new();
    private readonly InMemoryLinkStore _links = new();
    private readonly InMemoryMetadataStore _metadata = new();
    private readonly CapturingEmailSender _email = new();
    private readonly DeliveryService _service;
    private readonly AppRegEntity _app = new()
    {
        RowKey = "client-1",
        ObjectId = "object-1",
        DisplayName = "Test App",
        ContactEmails = "ops@customer.example",
    };

    public DeliveryServiceTests()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RegnrollOptions
        {
            PublicBaseUrl = "https://regnroll.example",
            TenantId = "tenant-1",
            LinkTtlDays = 14,
            SecretValidityDays = 180,
        });
        _metadata.Items[_app.RowKey] = _app;
        _service = new DeliveryService(
            _graph, _links, _metadata, new TemplateService(new InMemoryTemplateOverrideStore()), _email,
            new CryptoService(), options, _clock, NullLogger<DeliveryService>.Instance);
    }

    private (string Id, string Key) ExtractLink()
    {
        var body = Assert.Single(_email.Sent).HtmlBody;
        var start = body.IndexOf("https://regnroll.example/s/", StringComparison.Ordinal);
        Assert.True(start >= 0, "delivery URL missing from email");
        var url = body[start..body.IndexOfAny(['"', '<', ' ', '\n'], start)];
        var parts = url["https://regnroll.example/s/".Length..].Split('#');
        return (parts[0], parts[1]);
    }

    [Fact]
    public async Task SecretDelivery_EmailContainsOneTimeUrl_ClaimReturnsSecretExactlyOnce()
    {
        await _service.StartSecretDeliveryAsync(_app, oldCredentialExpiresAt: null);

        var (id, key) = ExtractLink();
        var stored = await _links.FindByRawIdAsync(id);
        Assert.NotNull(stored);
        Assert.NotEqual(id, stored!.RowKey);
        Assert.DoesNotContain("generated-secret-value", stored.Ciphertext);

        var first = await _service.ClaimAsync(id, key);
        Assert.Equal(ClaimStatus.Success, first.Status);
        Assert.Equal("generated-secret-value", first.Secret);
        Assert.Equal("client-1", first.ClientId);

        var second = await _service.ClaimAsync(id, key);
        Assert.Equal(ClaimStatus.Gone, second.Status);

        var receipt = await _links.FindByRawIdAsync(id);
        Assert.Equal(LinkStatuses.Claimed, receipt!.Status);
        Assert.Null(receipt.Ciphertext);
    }

    [Fact]
    public async Task Claim_WithWrongKey_DoesNotBurnThePayload()
    {
        await _service.StartSecretDeliveryAsync(_app, null);
        var (id, key) = ExtractLink();

        var wrong = await _service.ClaimAsync(id, new CryptoService().GenerateKey());
        Assert.Equal(ClaimStatus.InvalidKey, wrong.Status);

        var stillThere = await _service.ClaimAsync(id, key);
        Assert.Equal(ClaimStatus.Success, stillThere.Status);
    }

    [Fact]
    public async Task Claim_AfterLinkExpiry_ReturnsExpired()
    {
        await _service.StartSecretDeliveryAsync(_app, null);
        var (id, key) = ExtractLink();

        _clock.Advance(TimeSpan.FromDays(15));

        Assert.Equal(ClaimStatus.Expired, (await _service.ClaimAsync(id, key)).Status);
    }

    [Fact]
    public async Task LinkExpiry_IsCappedAtOldCredentialExpiry()
    {
        var oldExpiry = T0.AddDays(5);
        var link = await _service.StartSecretDeliveryAsync(_app, oldExpiry);

        Assert.Equal(oldExpiry, link.ExpiresAt);
    }

    [Fact]
    public async Task EmailFailure_RollsBackSecretAndLink()
    {
        _email.ThrowOnSend = new InvalidOperationException("smtp down");

        await Assert.ThrowsAsync<DeliveryException>(() => _service.StartSecretDeliveryAsync(_app, null));

        Assert.Single(_graph.RemovedSecrets);
        Assert.Empty(await _links.GetByClientAsync("client-1"));
    }

    [Fact]
    public async Task ManualRetrigger_SupersedesPriorPendingLink()
    {
        await _service.StartSecretDeliveryAsync(_app, null);
        var (firstId, firstKey) = ExtractLink();

        _email.Sent.Clear();
        await _service.StartSecretDeliveryAsync(_app, null);

        Assert.Equal(ClaimStatus.Gone, (await _service.ClaimAsync(firstId, firstKey)).Status);
        var (secondId, secondKey) = ExtractLink();
        Assert.Equal(ClaimStatus.Success, (await _service.ClaimAsync(secondId, secondKey)).Status);
    }

    [Fact]
    public async Task ConcurrentClaims_ExactlyOneWins()
    {
        await _service.StartSecretDeliveryAsync(_app, null);
        var (id, key) = ExtractLink();

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Task.Run(() => _service.ClaimAsync(id, key))));

        Assert.Equal(1, results.Count(r => r.Status == ClaimStatus.Success));
        Assert.Equal(7, results.Count(r => r.Status == ClaimStatus.Gone));
    }

    // ---------- certificate upload ----------

    private static string MakeCertPem()
    {
        // Validity is anchored to the fake clock (T0), not wall time — validation runs at T0.
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=upload-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(T0.AddDays(-1), T0.AddYears(1));
        return cert.ExportCertificatePem();
    }

    private async Task<string> StartUpload()
    {
        _email.Sent.Clear();
        await _service.StartCertificateUploadAsync(_app, null);
        var body = Assert.Single(_email.Sent).HtmlBody;
        var start = body.IndexOf("https://regnroll.example/c/", StringComparison.Ordinal);
        var url = body[start..body.IndexOfAny(['"', '<', ' ', '\n'], start)];
        return url["https://regnroll.example/c/".Length..];
    }

    [Fact]
    public async Task Upload_ValidCertificate_AddsViaGraphAndConsumesLink()
    {
        var token = await StartUpload();

        var result = await _service.UploadAsync(token, MakeCertPem());
        Assert.Equal(UploadStatus.Success, result.Status);
        Assert.Single(_graph.AddedCertificates);

        var again = await _service.UploadAsync(token, MakeCertPem());
        Assert.Equal(UploadStatus.Gone, again.Status);
    }

    [Fact]
    public async Task Upload_InvalidContent_DoesNotConsumeLink()
    {
        var token = await StartUpload();

        var bad = await _service.UploadAsync(token, "garbage");
        Assert.Equal(UploadStatus.InvalidCertificate, bad.Status);
        Assert.NotNull(bad.Error);
        Assert.Empty(_graph.AddedCertificates);

        Assert.Equal(UploadStatus.Success, (await _service.UploadAsync(token, MakeCertPem())).Status);
    }

    [Fact]
    public async Task Upload_GraphFailure_RevertsLinkToPending()
    {
        var token = await StartUpload();
        _graph.ThrowOnAddCertificate = new RegnrollGraphException("denied", 403);

        await Assert.ThrowsAsync<RegnrollGraphException>(() => _service.UploadAsync(token, MakeCertPem()));

        _graph.ThrowOnAddCertificate = null;
        Assert.Equal(UploadStatus.Success, (await _service.UploadAsync(token, MakeCertPem())).Status);
    }

    [Fact]
    public async Task Upload_ForUnlinkedApp_ReturnsGone()
    {
        var token = await StartUpload();
        _metadata.Items.Clear();

        Assert.Equal(UploadStatus.Gone, (await _service.UploadAsync(token, MakeCertPem())).Status);
    }
}
