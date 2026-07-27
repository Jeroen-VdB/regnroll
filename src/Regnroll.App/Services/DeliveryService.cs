using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Regnroll.App.Models;
using Regnroll.App.Options;

namespace Regnroll.App.Services;

public enum ClaimStatus { Success, Gone, Expired, InvalidKey }

public record ClaimResult(ClaimStatus Status, string? Secret = null, string? ClientId = null, DateTimeOffset? NewSecretExpiresAt = null);

public enum UploadStatus { Success, Gone, Expired, InvalidCertificate }

public record UploadResult(UploadStatus Status, string? Error = null, string? Thumbprint = null, DateTimeOffset? NotAfter = null);

/// <summary>Thrown when a flow fails after partial work; compensation has already run.</summary>
public class DeliveryException(string message, Exception? inner = null) : Exception(message, inner);

public interface IDeliveryService
{
    /// <summary>Creates a new client secret and emails a one-time retrieval link. Supersedes any pending secret link.</summary>
    Task<LinkEntity> StartSecretDeliveryAsync(AppRegEntity app, DateTimeOffset? oldCredentialExpiresAt, CancellationToken ct = default);

    /// <summary>Creates a certificate upload link and emails it. Supersedes any pending upload link.</summary>
    Task<LinkEntity> StartCertificateUploadAsync(AppRegEntity app, DateTimeOffset? oldCredentialExpiresAt, CancellationToken ct = default);

    Task<ClaimResult> ClaimAsync(string linkId, string key, CancellationToken ct = default);

    Task<UploadResult> UploadAsync(string token, string certificateContent, CancellationToken ct = default);
}

public sealed class DeliveryService(
    IGraphAppService graphService,
    ILinkStore linkStore,
    IMetadataStore metadataStore,
    ITemplateService templates,
    IEmailSender email,
    CryptoService crypto,
    IOptions<RegnrollOptions> options,
    TimeProvider clock,
    ILogger<DeliveryService> logger) : IDeliveryService
{
    public async Task<LinkEntity> StartSecretDeliveryAsync(AppRegEntity app, DateTimeOffset? oldCredentialExpiresAt, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        await linkStore.InvalidatePendingAsync(app.ClientId, CredentialTypes.Secret, ct);

        var created = await graphService.AddSecretAsync(
            app.ObjectId, $"regnroll-{now:yyyyMMdd-HHmm}", now.AddDays(options.Value.SecretValidityDays), ct);

        var linkId = crypto.GenerateLinkId();
        var key = crypto.GenerateKey();
        var (nonce, ciphertext) = crypto.Encrypt(created.SecretText, key);
        var expiresAt = LinkExpiry(now, oldCredentialExpiresAt);

        LinkEntity link;
        try
        {
            link = await linkStore.CreateSecretLinkAsync(
                linkId, app.ClientId, ciphertext, nonce, now, expiresAt,
                created.KeyId, created.ExpiresAt, oldCredentialExpiresAt, ct);
        }
        catch (Exception e)
        {
            await CompensateSecret(app, created.KeyId, rowKey: null, ct);
            throw new DeliveryException($"Failed to store the delivery link for {app.ClientId}; the created secret was rolled back.", e);
        }

        try
        {
            var url = $"{options.Value.PublicBaseUrl.TrimEnd('/')}/s/{linkId}#{key}";
            await SendTemplateAsync(TemplateKeys.NewSecret, app, CredentialTypes.Secret, url, oldCredentialExpiresAt ?? created.ExpiresAt, ct);
        }
        catch (Exception e)
        {
            await CompensateSecret(app, created.KeyId, link.RowKey, ct);
            throw new DeliveryException($"Failed to send the delivery email for {app.ClientId}; the created secret and link were rolled back. Cause: {e.Message}", e);
        }

        logger.LogInformation("Secret delivery started for {ClientId} (link row {RowKey}, expires {ExpiresAt}).", app.ClientId, link.RowKey, expiresAt);
        return link;
    }

    public async Task<LinkEntity> StartCertificateUploadAsync(AppRegEntity app, DateTimeOffset? oldCredentialExpiresAt, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        await linkStore.InvalidatePendingAsync(app.ClientId, CredentialTypes.Certificate, ct);

        var token = crypto.GenerateLinkId();
        var expiresAt = LinkExpiry(now, oldCredentialExpiresAt);
        var link = await linkStore.CreateUploadLinkAsync(token, app.ClientId, now, expiresAt, oldCredentialExpiresAt, ct);

        try
        {
            var url = $"{options.Value.PublicBaseUrl.TrimEnd('/')}/c/{token}";
            await SendTemplateAsync(TemplateKeys.NewCertificate, app, CredentialTypes.Certificate, url, oldCredentialExpiresAt, ct);
        }
        catch (Exception e)
        {
            await linkStore.DeleteAsync(link.RowKey, ct);
            throw new DeliveryException($"Failed to send the upload email for {app.ClientId}; the upload link was rolled back. Cause: {e.Message}", e);
        }

        logger.LogInformation("Certificate upload link created for {ClientId} (row {RowKey}, expires {ExpiresAt}).", app.ClientId, link.RowKey, expiresAt);
        return link;
    }

    public async Task<ClaimResult> ClaimAsync(string linkId, string key, CancellationToken ct = default)
    {
        var link = await linkStore.FindByRawIdAsync(linkId, ct);
        if (link is null || link.Type != CredentialTypes.Secret || !link.IsPending || link.Ciphertext is null || link.Nonce is null)
        {
            return new ClaimResult(ClaimStatus.Gone);
        }

        if (link.IsExpired(clock.GetUtcNow()))
        {
            return new ClaimResult(ClaimStatus.Expired);
        }

        string secret;
        try
        {
            secret = crypto.Decrypt(link.Nonce, link.Ciphertext, key);
        }
        catch (Exception e) when (e is CryptographicException or FormatException)
        {
            // Wrong key: reject WITHOUT consuming — the ciphertext row stays intact.
            return new ClaimResult(ClaimStatus.InvalidKey);
        }

        if (!await linkStore.TryCompleteAsync(link, LinkStatuses.Claimed, clock.GetUtcNow(), ct))
        {
            // A concurrent claim won the ETag race: exactly one caller ever receives the secret.
            return new ClaimResult(ClaimStatus.Gone);
        }

        logger.LogInformation("Secret claimed for {ClientId} (row {RowKey}).", link.ClientId, link.RowKey);
        return new ClaimResult(ClaimStatus.Success, secret, link.ClientId, link.NewCredentialExpiresAt);
    }

    public async Task<UploadResult> UploadAsync(string token, string certificateContent, CancellationToken ct = default)
    {
        var link = await linkStore.FindByRawIdAsync(token, ct);
        if (link is null || link.Type != CredentialTypes.Certificate || !link.IsPending)
        {
            return new UploadResult(UploadStatus.Gone);
        }

        var now = clock.GetUtcNow();
        if (link.IsExpired(now))
        {
            return new UploadResult(UploadStatus.Expired);
        }

        var validation = CertificateValidator.Validate(certificateContent, now);
        if (!validation.IsValid)
        {
            // Rejections never consume the link.
            return new UploadResult(UploadStatus.InvalidCertificate, validation.ErrorMessage);
        }

        var app = await metadataStore.GetAsync(link.ClientId, ct);
        if (app is null)
        {
            return new UploadResult(UploadStatus.Gone);
        }

        // Consume the link first (ETag race → exactly one upload), then write to Graph;
        // a Graph failure reverts the link so the customer can retry.
        if (!await linkStore.TryCompleteAsync(link, LinkStatuses.Uploaded, now, ct))
        {
            return new UploadResult(UploadStatus.Gone);
        }

        try
        {
            var thumbprint = await graphService.AddCertificateAsync(
                app.ObjectId, validation.Certificate!, $"regnroll-upload-{now:yyyyMMdd-HHmm}", ct);
            logger.LogInformation("Certificate {Thumbprint} added for {ClientId}.", thumbprint, link.ClientId);
            return new UploadResult(UploadStatus.Success, Thumbprint: thumbprint, NotAfter: validation.Certificate!.NotAfter.ToUniversalTime());
        }
        catch (Exception)
        {
            await linkStore.TryRevertToPendingAsync(link, ct);
            throw;
        }
    }

    private DateTimeOffset LinkExpiry(DateTimeOffset now, DateTimeOffset? oldCredentialExpiresAt)
    {
        var expiry = now.AddDays(options.Value.LinkTtlDays);
        return oldCredentialExpiresAt is { } old && old < expiry && old > now ? old : expiry;
    }

    private async Task SendTemplateAsync(
        string templateKey, AppRegEntity app, string credentialType, string url, DateTimeOffset? expiryDate, CancellationToken ct)
    {
        var template = await templates.GetAsync(templateKey, ct);
        var variables = BuildVariables(app, credentialType, url, expiryDate);
        await email.SendAsync(app.GetContacts(), templates.Render(template.Subject, variables), templates.Render(template.HtmlBody, variables), ct);
    }

    internal Dictionary<string, string> BuildVariables(AppRegEntity app, string credentialType, string url, DateTimeOffset? expiryDate)
    {
        var tenantId = options.Value.TenantId;
        return new Dictionary<string, string>
        {
            [TemplateVariables.Url] = url,
            [TemplateVariables.CredentialType] = credentialType,
            [TemplateVariables.ExpiryDate] = expiryDate?.ToString("yyyy-MM-dd") ?? "n/a",
            [TemplateVariables.ClientId] = app.ClientId,
            [TemplateVariables.ClientName] = app.DisplayName,
            [TemplateVariables.TokenEndpoint] = string.IsNullOrWhiteSpace(tenantId)
                ? ""
                : $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token",
        };
    }

    private async Task CompensateSecret(AppRegEntity app, Guid keyId, string? rowKey, CancellationToken ct)
    {
        try
        {
            if (rowKey is not null)
            {
                await linkStore.DeleteAsync(rowKey, ct);
            }

            await graphService.RemoveSecretAsync(app.ObjectId, keyId, ct);
            logger.LogWarning("Compensated failed secret delivery for {ClientId}: secret {KeyId} removed.", app.ClientId, keyId);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Compensation failed for {ClientId}, secret {KeyId} may be orphaned (it expires on its own).", app.ClientId, keyId);
        }
    }
}
