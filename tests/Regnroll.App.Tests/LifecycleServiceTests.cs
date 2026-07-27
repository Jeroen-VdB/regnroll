using Microsoft.Extensions.Logging.Abstractions;
using Regnroll.App.Models;
using Regnroll.App.Options;
using Regnroll.App.Services;
using Xunit;

namespace Regnroll.App.Tests;

public class LifecycleServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 1, 6, 0, 0, TimeSpan.Zero);

    private readonly FixedTimeProvider _clock = new(T0);
    private readonly FakeGraphAppService _graph = new();
    private readonly InMemoryLinkStore _links = new();
    private readonly InMemoryMetadataStore _metadata = new();
    private readonly CapturingEmailSender _email = new();
    private readonly LifecycleService _service;
    private readonly AppRegEntity _app = new()
    {
        RowKey = "client-1",
        ObjectId = "object-1",
        DisplayName = "Test App",
        ContactEmails = "ops@customer.example",
    };

    public LifecycleServiceTests()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RegnrollOptions
        {
            PublicBaseUrl = "https://regnroll.example",
            RotateBeforeDays = 30,
            WarnBeforeDays = 7,
            LinkTtlDays = 14,
        });
        _metadata.Items[_app.RowKey] = _app;
        var delivery = new DeliveryService(
            _graph, _links, _metadata, new TemplateService(new InMemoryTemplateOverrideStore()), _email,
            new CryptoService(), options, _clock, NullLogger<DeliveryService>.Instance);
        _service = new LifecycleService(
            _metadata, _links, _graph, delivery, new TemplateService(new InMemoryTemplateOverrideStore()), _email,
            options, _clock, NullLogger<LifecycleService>.Instance);
    }

    private void SetGraphApp(params (string Type, DateTimeOffset End)[] credentials)
    {
        _graph.Apps["client-1"] = new AppRegistration(
            "object-1", "client-1", "Test App",
            credentials.Where(c => c.Type == CredentialTypes.Secret)
                .Select(c => new CredentialInfo(Guid.NewGuid(), "s", null, c.End)).ToList(),
            credentials.Where(c => c.Type == CredentialTypes.Certificate)
                .Select(c => new CredentialInfo(Guid.NewGuid(), "c", null, c.End)).ToList());
    }

    [Fact]
    public async Task SecretInsideRotateWindow_TriggersRotation_Idempotently()
    {
        SetGraphApp((CredentialTypes.Secret, T0.AddDays(20)));

        var first = await _service.RunAsync();
        Assert.Equal(1, first.SecretsRotated);
        Assert.Single(_email.Sent);

        var second = await _service.RunAsync();
        Assert.Equal(0, second.SecretsRotated);
        Assert.Single(_email.Sent);
    }

    [Fact]
    public async Task SecretOutsideRotateWindow_IsLeftAlone()
    {
        SetGraphApp((CredentialTypes.Secret, T0.AddDays(120)));

        var summary = await _service.RunAsync();

        Assert.Equal(0, summary.SecretsRotated);
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task CertificateInsideRotateWindow_SendsUploadLink()
    {
        SetGraphApp((CredentialTypes.Certificate, T0.AddDays(10)));

        var summary = await _service.RunAsync();

        Assert.Equal(1, summary.CertificateRequestsSent);
        Assert.Contains("/c/", Assert.Single(_email.Sent).HtmlBody);
    }

    [Fact]
    public async Task PerAppOverride_ControlsTheRotateWindow()
    {
        _app.RotateBeforeDaysOverride = 10;
        SetGraphApp((CredentialTypes.Secret, T0.AddDays(20)));

        Assert.Equal(0, (await _service.RunAsync()).SecretsRotated);

        _app.RotateBeforeDaysOverride = null;
        Assert.Equal(1, (await _service.RunAsync()).SecretsRotated);
    }

    [Fact]
    public async Task PendingUnwarnedLink_InsideWarnWindow_IsWarnedExactlyOnce()
    {
        SetGraphApp((CredentialTypes.Secret, T0.AddDays(120)));
        await _links.CreateSecretLinkAsync(
            "raw-id", "client-1", "ct", "nonce", T0.AddDays(-25), T0.AddDays(3),
            Guid.NewGuid(), T0.AddDays(180), oldCredentialExpiresAt: T0.AddDays(5));

        var first = await _service.RunAsync();
        Assert.Equal(1, first.WarningsSent);
        var warning = Assert.Single(_email.Sent);
        Assert.Contains("Reminder", warning.Subject);

        _email.Sent.Clear();
        var second = await _service.RunAsync();
        Assert.Equal(0, second.WarningsSent);
        Assert.Empty(_email.Sent);
    }

    [Fact]
    public async Task PendingLink_OutsideWarnWindow_IsNotWarned()
    {
        SetGraphApp((CredentialTypes.Secret, T0.AddDays(120)));
        await _links.CreateSecretLinkAsync(
            "raw-id", "client-1", "ct", "nonce", T0, T0.AddDays(14),
            Guid.NewGuid(), T0.AddDays(180), oldCredentialExpiresAt: T0.AddDays(25));

        Assert.Equal(0, (await _service.RunAsync()).WarningsSent);
    }

    [Fact]
    public async Task ExpiredSecret_IsRemovedAndNotified_ValidSecretIsNever()
    {
        SetGraphApp(
            (CredentialTypes.Secret, T0.AddDays(-1)),
            (CredentialTypes.Secret, T0.AddDays(120)));

        var summary = await _service.RunAsync();

        Assert.Equal(1, summary.CredentialsRemoved);
        Assert.Single(_graph.RemovedSecrets);
        Assert.Contains(_email.Sent, m => m.Subject.Contains("expired"));
    }

    [Fact]
    public async Task ValidCredentials_AreNeverDeleted()
    {
        SetGraphApp(
            (CredentialTypes.Secret, T0.AddDays(2)),
            (CredentialTypes.Certificate, T0.AddDays(2)));

        await _service.RunAsync();

        Assert.Empty(_graph.RemovedSecrets);
    }

    [Fact]
    public async Task ExpiredCertificates_AreRemovedViaGraphAndNotified()
    {
        SetGraphApp((CredentialTypes.Certificate, T0.AddDays(120)));
        _graph.ExpiredCertificatesToRemove = [new CredentialInfo(Guid.NewGuid(), "old-cert", null, T0.AddDays(-2))];

        var summary = await _service.RunAsync();

        Assert.Equal(1, summary.CredentialsRemoved);
        Assert.Contains(_email.Sent, m => m.Subject.Contains("certificate"));
    }

    [Fact]
    public async Task ExpiredLinkRows_ArePurged()
    {
        SetGraphApp((CredentialTypes.Secret, T0.AddDays(120)));
        await _links.CreateUploadLinkAsync("dead-token", "client-1", T0.AddDays(-30), T0.AddDays(-1), null);

        var summary = await _service.RunAsync();

        Assert.Equal(1, summary.LinksPurged);
        Assert.Null(await _links.FindByRawIdAsync("dead-token"));
    }

    [Fact]
    public async Task PerAppGraphFailure_DoesNotAbortTheWholeScan()
    {
        var broken = new AppRegEntity { RowKey = "client-2", ObjectId = "object-2", DisplayName = "Broken", ContactEmails = "x@y.z" };
        _metadata.Items[broken.RowKey] = broken; // no graph app registered -> GetByClientIdAsync throws 404
        SetGraphApp((CredentialTypes.Secret, T0.AddDays(20)));

        var summary = await _service.RunAsync();

        Assert.Equal(1, summary.Errors);
        Assert.Equal(1, summary.SecretsRotated);
    }
}
