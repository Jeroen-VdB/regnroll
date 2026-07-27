using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Options;
using Regnroll.App.Infrastructure;
using Regnroll.App.Models;
using Regnroll.App.Options;
using Regnroll.App.Services;

namespace Regnroll.App.Functions.Http;

public record AdminLinkDto(string Type, string Status, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset? CompletedAt, DateTimeOffset? WarnedAt, bool Expired);

public record CredentialSummaryDto(int Count, DateTimeOffset? LatestExpiry);

public record AdminAppDto(
    string ClientId,
    string DisplayName,
    bool Linked,
    bool Manageable,
    string? ContactEmails,
    int RotateBeforeDays,
    int WarnBeforeDays,
    bool RotateOverridden,
    bool WarnOverridden,
    CredentialSummaryDto Secrets,
    CredentialSummaryDto Certificates,
    IReadOnlyList<AdminLinkDto> Links);

public record LinkRequest(string? ContactEmails, bool CreateSecret, bool RequestCertificate);

public record SettingsRequest(int? RotateBeforeDays, int? WarnBeforeDays, string? ContactEmails);

/// <summary>Admin API for the app registration registry. Never returns secret material.</summary>
public class AdminApps(
    IGraphAppService graphService,
    IMetadataStore metadataStore,
    ILinkStore linkStore,
    IDeliveryService delivery,
    IOptions<RegnrollOptions> options,
    TimeProvider clock)
{
    [Function($"{nameof(AdminApps)}_{nameof(List)}")]
    public async Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/admin/apps")] HttpRequest req,
        CancellationToken ct)
    {
        if (!AdminAuth.IsAuthorized(req))
        {
            return AdminAuth.Unauthorized();
        }

        IReadOnlyList<AppRegistration> manageable;
        try
        {
            manageable = await graphService.ListManageableAsync(ct);
        }
        catch (RegnrollGraphException e)
        {
            return Problem(e);
        }

        var now = clock.GetUtcNow();
        var linked = (await metadataStore.GetAllAsync(ct)).ToDictionary(a => a.ClientId);
        var linksByClient = (await linkStore.GetAllAsync(ct)).GroupBy(l => l.ClientId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<AdminAppDto>();
        foreach (var app in manageable.OrderBy(a => a.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            linked.Remove(app.ClientId, out var meta);
            result.Add(ToDto(app, meta, manageableInGraph: true, linksByClient, now));
        }

        // Still-linked apps the identity can no longer see (ownership removed, app deleted): surface, don't hide.
        foreach (var meta in linked.Values.OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            result.Add(ToDto(new AppRegistration(meta.ObjectId, meta.ClientId, meta.DisplayName, [], []), meta, manageableInGraph: false, linksByClient, now));
        }

        return new OkObjectResult(result);
    }

    [Function($"{nameof(AdminApps)}_{nameof(Link)}")]
    public async Task<IActionResult> Link(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/admin/apps/{clientId}/link")] HttpRequest req,
        string clientId,
        CancellationToken ct)
    {
        if (!AdminAuth.IsAuthorized(req))
        {
            return AdminAuth.Unauthorized();
        }

        var body = await req.ReadFromJsonAsync<LinkRequest>(ct);
        var contacts = ParseContacts(body?.ContactEmails);
        if (contacts is null)
        {
            return new BadRequestObjectResult(new { error = "invalid_contacts", message = "Provide at least one valid contact email address (semicolon-separated)." });
        }

        AppRegistration graphApp;
        try
        {
            graphApp = await graphService.GetByClientIdAsync(clientId, ct);
        }
        catch (RegnrollGraphException e)
        {
            return Problem(e);
        }

        var entity = new AppRegEntity
        {
            RowKey = graphApp.ClientId,
            ObjectId = graphApp.ObjectId,
            DisplayName = graphApp.DisplayName,
            ContactEmails = contacts,
            LinkedAt = clock.GetUtcNow(),
        };
        await metadataStore.UpsertAsync(entity, ct);

        try
        {
            if (body?.CreateSecret == true)
            {
                await delivery.StartSecretDeliveryAsync(entity, graphApp.LatestExpiring(CredentialTypes.Secret)?.EndDateTime, ct);
            }

            if (body?.RequestCertificate == true)
            {
                await delivery.StartCertificateUploadAsync(entity, graphApp.LatestExpiring(CredentialTypes.Certificate)?.EndDateTime, ct);
            }
        }
        catch (Exception e) when (e is DeliveryException or RegnrollGraphException)
        {
            return new ObjectResult(new { error = "delivery_failed", linked = true, message = e.Message }) { StatusCode = StatusCodes.Status502BadGateway };
        }

        return new OkObjectResult(new { linked = true });
    }

    [Function($"{nameof(AdminApps)}_{nameof(Unlink)}")]
    public async Task<IActionResult> Unlink(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/admin/apps/{clientId}/unlink")] HttpRequest req,
        string clientId,
        CancellationToken ct)
    {
        if (!AdminAuth.IsAuthorized(req))
        {
            return AdminAuth.Unauthorized();
        }

        // Metadata and links only — the app registration and its credentials are never touched.
        await linkStore.DeleteByClientAsync(clientId, ct);
        await metadataStore.DeleteAsync(clientId, ct);
        return new OkObjectResult(new { unlinked = true });
    }

    [Function($"{nameof(AdminApps)}_{nameof(UpdateSettings)}")]
    public async Task<IActionResult> UpdateSettings(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "api/admin/apps/{clientId}/settings")] HttpRequest req,
        string clientId,
        CancellationToken ct)
    {
        if (!AdminAuth.IsAuthorized(req))
        {
            return AdminAuth.Unauthorized();
        }

        var entity = await metadataStore.GetAsync(clientId, ct);
        if (entity is null)
        {
            return new NotFoundObjectResult(new { error = "not_linked", message = "This app registration is not linked." });
        }

        var body = await req.ReadFromJsonAsync<SettingsRequest>(ct);
        if (body is null)
        {
            return new BadRequestObjectResult(new { error = "invalid_request", message = "A JSON body is required." });
        }

        if (body.RotateBeforeDays is < 1 or > 365 || body.WarnBeforeDays is < 1 or > 365)
        {
            return new BadRequestObjectResult(new { error = "invalid_range", message = "rotateBeforeDays and warnBeforeDays must be between 1 and 365 (or null for the default)." });
        }

        if (body.ContactEmails is not null)
        {
            var contacts = ParseContacts(body.ContactEmails);
            if (contacts is null)
            {
                return new BadRequestObjectResult(new { error = "invalid_contacts", message = "Provide at least one valid contact email address." });
            }

            entity.ContactEmails = contacts;
        }

        // Full-replace semantics for the overrides: null clears the override, the default applies again.
        entity.RotateBeforeDaysOverride = body.RotateBeforeDays;
        entity.WarnBeforeDaysOverride = body.WarnBeforeDays;
        await metadataStore.UpsertAsync(entity, ct);

        var effective = MetadataStore.Resolve(entity, options.Value);
        return new OkObjectResult(new { rotateBeforeDays = effective.RotateBeforeDays, warnBeforeDays = effective.WarnBeforeDays });
    }

    private AdminAppDto ToDto(
        AppRegistration app, AppRegEntity? meta, bool manageableInGraph,
        IReadOnlyDictionary<string, List<LinkEntity>> linksByClient, DateTimeOffset now)
    {
        var settings = meta is null
            ? new EffectiveSettings(options.Value.RotateBeforeDays, options.Value.WarnBeforeDays)
            : MetadataStore.Resolve(meta, options.Value);
        var links = linksByClient.TryGetValue(app.ClientId, out var list)
            ? list.OrderByDescending(l => l.CreatedAt)
                  .Select(l => new AdminLinkDto(l.Type, l.Status, l.CreatedAt, l.ExpiresAt, l.CompletedAt, l.WarnedAt, l.IsExpired(now)))
                  .ToList()
            : [];

        return new AdminAppDto(
            app.ClientId,
            meta?.DisplayName is { Length: > 0 } cached && app.DisplayName.Length == 0 ? cached : app.DisplayName,
            Linked: meta is not null,
            Manageable: manageableInGraph,
            meta?.ContactEmails,
            settings.RotateBeforeDays,
            settings.WarnBeforeDays,
            RotateOverridden: meta?.RotateBeforeDaysOverride is not null,
            WarnOverridden: meta?.WarnBeforeDaysOverride is not null,
            new CredentialSummaryDto(app.Secrets.Count, app.LatestExpiring(CredentialTypes.Secret)?.EndDateTime),
            new CredentialSummaryDto(app.Certificates.Count, app.LatestExpiring(CredentialTypes.Certificate)?.EndDateTime),
            links);
    }

    private static string? ParseContacts(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var parts = raw.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length > 0 && parts.All(p => p.Contains('@') && p.Length >= 5) ? string.Join(';', parts) : null;
    }

    private static ObjectResult Problem(RegnrollGraphException e) =>
        new(new { error = "graph_error", message = e.Message }) { StatusCode = e.StatusCode is >= 400 and < 600 ? e.StatusCode : 502 };
}
