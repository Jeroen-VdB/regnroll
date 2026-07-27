using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Regnroll.App.Models;
using Regnroll.App.Options;

namespace Regnroll.App.Services;

public record LifecycleSummary(
    int AppsScanned,
    int SecretsRotated,
    int CertificateRequestsSent,
    int WarningsSent,
    int CredentialsRemoved,
    int LinksPurged,
    int Errors);

public interface ILifecycleService
{
    Task<LifecycleSummary> RunAsync(CancellationToken ct = default);
}

/// <summary>
/// The daily scan: rotate-before, warn-before ("not-opened" reminders), expired cleanup, link purge.
/// All state is re-derived from Graph + the tables each run; idempotency comes from pending-link checks.
/// </summary>
public sealed class LifecycleService(
    IMetadataStore metadataStore,
    ILinkStore linkStore,
    IGraphAppService graphService,
    IDeliveryService delivery,
    ITemplateService templates,
    IEmailSender email,
    IOptions<RegnrollOptions> options,
    TimeProvider clock,
    ILogger<LifecycleService> logger) : ILifecycleService
{
    public async Task<LifecycleSummary> RunAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var apps = await metadataStore.GetAllAsync(ct);
        // Snapshot taken before any creations this run, so links we create below
        // are neither warned about nor counted as pending for their own rotation check.
        var linkSnapshot = await linkStore.GetAllAsync(ct);

        int rotated = 0, certRequests = 0, warnings = 0, removed = 0, errors = 0;

        foreach (var app in apps)
        {
            try
            {
                var graphApp = await graphService.GetByClientIdAsync(app.ClientId, ct);
                var settings = MetadataStore.Resolve(app, options.Value);

                foreach (var type in new[] { CredentialTypes.Secret, CredentialTypes.Certificate })
                {
                    var pending = linkSnapshot
                        .Where(l => l.ClientId == app.ClientId && l.Type == type && l.IsPending && !l.IsExpired(now))
                        .ToList();

                    // Rotate: latest-expiring credential inside (or past) the rotate window, no pending delivery yet.
                    var latest = graphApp.LatestExpiring(type);
                    if (latest?.EndDateTime is { } end && end <= now.AddDays(settings.RotateBeforeDays) && pending.Count == 0)
                    {
                        if (type == CredentialTypes.Secret)
                        {
                            await delivery.StartSecretDeliveryAsync(app, end, ct);
                            rotated++;
                        }
                        else
                        {
                            await delivery.StartCertificateUploadAsync(app, end, ct);
                            certRequests++;
                        }
                    }

                    // Warn: pending link, old credential inside the warn window, not warned yet. At most once per link.
                    foreach (var link in pending)
                    {
                        if (link.WarnedAt is null
                            && link.OldCredentialExpiresAt is { } oldEnd
                            && oldEnd <= now.AddDays(settings.WarnBeforeDays))
                        {
                            await SendNotificationAsync(TemplateKeys.Warning, app, type, oldEnd, ct);
                            await linkStore.MarkWarnedAsync(link, now, ct);
                            warnings++;
                        }
                    }

                    // Expired cleanup: only credentials already past expiry (cryptographically dead) are ever deleted.
                    removed += await CleanupExpiredAsync(app, graphApp, type, now, ct);
                }
            }
            catch (Exception e)
            {
                errors++;
                logger.LogError(e, "Lifecycle scan failed for {ClientId}; continuing with remaining apps.", app.ClientId);
            }
        }

        var purged = await linkStore.PurgeExpiredAsync(now, ct);
        var summary = new LifecycleSummary(apps.Count, rotated, certRequests, warnings, removed, purged, errors);
        logger.LogInformation(
            "Lifecycle scan done: {Apps} apps, {Rotated} secrets rotated, {CertReqs} cert requests, {Warnings} warnings, {Removed} expired credentials removed, {Purged} links purged, {Errors} errors.",
            summary.AppsScanned, summary.SecretsRotated, summary.CertificateRequestsSent, summary.WarningsSent, summary.CredentialsRemoved, summary.LinksPurged, summary.Errors);
        return summary;
    }

    private async Task<int> CleanupExpiredAsync(AppRegEntity app, AppRegistration graphApp, string type, DateTimeOffset now, CancellationToken ct)
    {
        var removedCredentials = new List<CredentialInfo>();

        if (type == CredentialTypes.Secret)
        {
            foreach (var credential in graphApp.Secrets.Where(c => c.IsExpired(now)))
            {
                await graphService.RemoveSecretAsync(app.ObjectId, credential.KeyId, ct);
                removedCredentials.Add(credential);
            }
        }
        else
        {
            removedCredentials.AddRange(await graphService.RemoveExpiredCertificatesAsync(app.ObjectId, now, ct));
        }

        foreach (var credential in removedCredentials)
        {
            try
            {
                await SendNotificationAsync(TemplateKeys.Expired, app, type, credential.EndDateTime, ct);
            }
            catch (Exception e)
            {
                // The credential was already dead and is gone either way; a failed notification must not undo that.
                logger.LogError(e, "Failed to send expired notification for {ClientId} ({Type}).", app.ClientId, type);
            }
        }

        return removedCredentials.Count;
    }

    private async Task SendNotificationAsync(string templateKey, AppRegEntity app, string type, DateTimeOffset? expiryDate, CancellationToken ct)
    {
        var template = await templates.GetAsync(templateKey, ct);
        var variables = new Dictionary<string, string>
        {
            [TemplateVariables.Url] = "",
            [TemplateVariables.CredentialType] = type,
            [TemplateVariables.ExpiryDate] = expiryDate?.ToString("yyyy-MM-dd") ?? "n/a",
            [TemplateVariables.ClientId] = app.ClientId,
            [TemplateVariables.ClientName] = app.DisplayName,
            [TemplateVariables.TokenEndpoint] = string.IsNullOrWhiteSpace(options.Value.TenantId)
                ? ""
                : $"https://login.microsoftonline.com/{options.Value.TenantId}/oauth2/v2.0/token",
        };
        await email.SendAsync(app.GetContacts(), templates.Render(template.Subject, variables), templates.Render(template.HtmlBody, variables), ct);
    }
}
