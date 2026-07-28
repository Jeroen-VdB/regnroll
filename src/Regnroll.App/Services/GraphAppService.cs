using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Applications.Item.AddPassword;
using Microsoft.Graph.Applications.Item.RemovePassword;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Regnroll.App.Models;
using Regnroll.App.Options;
using AppRegistrationModel = Regnroll.App.Models.AppRegistration;

namespace Regnroll.App.Services;

/// <summary>Graph failure translated into an admin-actionable message.</summary>
public class RegnrollGraphException(string message, int statusCode, Exception? inner = null) : Exception(message, inner)
{
    public int StatusCode { get; } = statusCode;
}

public interface IGraphAppService
{
    /// <summary>App registrations the identity can manage: owned objects (OwnedBy mode) or all applications (All mode).</summary>
    Task<IReadOnlyList<AppRegistrationModel>> ListManageableAsync(CancellationToken ct = default);

    Task<AppRegistrationModel> GetByClientIdAsync(string clientId, CancellationToken ct = default);

    Task<CreatedSecret> AddSecretAsync(string objectId, string displayName, DateTimeOffset endDateTime, CancellationToken ct = default);

    Task RemoveSecretAsync(string objectId, Guid keyId, CancellationToken ct = default);

    /// <summary>Adds a certificate by PATCHing the full keyCredentials array (addKey needs proof-of-possession, unusable app-only). Never removes existing entries.</summary>
    Task<string> AddCertificateAsync(string objectId, X509Certificate2 certificate, string displayName, CancellationToken ct = default);

    /// <summary>Removes only credentials that are already past expiry. Returns what was removed.</summary>
    Task<IReadOnlyList<CredentialInfo>> RemoveExpiredCertificatesAsync(string objectId, DateTimeOffset now, CancellationToken ct = default);
}

public sealed class GraphAppService(GraphServiceClient graph, IOptions<RegnrollOptions> options, ILogger<GraphAppService> logger) : IGraphAppService
{
    private static readonly string[] AppSelect = ["id", "appId", "displayName", "passwordCredentials", "keyCredentials"];

    public async Task<IReadOnlyList<AppRegistrationModel>> ListManageableAsync(CancellationToken ct = default)
    {
        var o = options.Value;
        var apps = new List<Application>();

        if (o.UseTenantWideMode)
        {
            var page = await Wrap("list all applications", () => graph.Applications.GetAsync(rc =>
            {
                rc.QueryParameters.Select = AppSelect;
                rc.QueryParameters.Top = 999;
            }, ct));
            await Iterate(page, apps, ct);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(o.ManagedIdentityPrincipalId))
            {
                throw new RegnrollGraphException(
                    "Regnroll__ManagedIdentityPrincipalId is not configured. In OwnedBy mode it must contain the object id of the managed identity's service principal (deployment sets this automatically).",
                    500);
            }

            var page = await Wrap("list owned app registrations", () =>
                graph.ServicePrincipals[o.ManagedIdentityPrincipalId].OwnedObjects.GraphApplication.GetAsync(rc =>
                {
                    rc.QueryParameters.Select = AppSelect;
                    rc.QueryParameters.Top = 999;
                }, ct));
            await Iterate(page, apps, ct);
        }

        // Without the Graph app role, ownedObjects can still succeed but returns limited
        // profiles (id only, no appId/displayName) — fail with guidance instead of
        // handing the UI unusable rows.
        if (apps.Count > 0 && apps.All(a => string.IsNullOrEmpty(a.AppId)))
        {
            throw new RegnrollGraphException(GraphErrorMapper.LimitedProfile(o.UseTenantWideMode), 403);
        }

        return apps.Select(ToModel).ToList();
    }

    public async Task<AppRegistrationModel> GetByClientIdAsync(string clientId, CancellationToken ct = default)
    {
        var app = await Wrap($"read app registration {clientId}", () =>
            graph.ApplicationsWithAppId(clientId).GetAsync(rc => rc.QueryParameters.Select = AppSelect, ct));
        return ToModel(app!);
    }

    public async Task<CreatedSecret> AddSecretAsync(string objectId, string displayName, DateTimeOffset endDateTime, CancellationToken ct = default)
    {
        var credential = await Wrap("create client secret", () => graph.Applications[objectId].AddPassword.PostAsync(
            new AddPasswordPostRequestBody
            {
                PasswordCredential = new PasswordCredential
                {
                    DisplayName = displayName,
                    EndDateTime = endDateTime,
                },
            }, cancellationToken: ct));

        return new CreatedSecret(
            credential!.KeyId ?? Guid.Empty,
            credential.SecretText ?? throw new RegnrollGraphException("Graph did not return the secret text.", 502),
            credential.EndDateTime ?? endDateTime);
    }

    public Task RemoveSecretAsync(string objectId, Guid keyId, CancellationToken ct = default) =>
        Wrap("remove client secret", () => graph.Applications[objectId].RemovePassword.PostAsync(
            new RemovePasswordPostRequestBody { KeyId = keyId }, cancellationToken: ct));

    public async Task<string> AddCertificateAsync(string objectId, X509Certificate2 certificate, string displayName, CancellationToken ct = default)
    {
        await PatchKeyCredentialsWithRetry(objectId, existing =>
        {
            existing.Add(new KeyCredential
            {
                Type = "AsymmetricX509Cert",
                Usage = "Verify",
                Key = certificate.RawData,
                DisplayName = displayName,
            });
            return existing;
        }, ct);

        return certificate.Thumbprint;
    }

    public async Task<IReadOnlyList<CredentialInfo>> RemoveExpiredCertificatesAsync(string objectId, DateTimeOffset now, CancellationToken ct = default)
    {
        var removed = new List<CredentialInfo>();
        await PatchKeyCredentialsWithRetry(objectId, existing =>
        {
            removed.Clear();
            var keep = new List<KeyCredential>();
            foreach (var kc in existing)
            {
                if (kc.EndDateTime is { } end && end <= now)
                {
                    removed.Add(new CredentialInfo(kc.KeyId ?? Guid.Empty, kc.DisplayName, kc.StartDateTime, kc.EndDateTime));
                }
                else
                {
                    keep.Add(kc);
                }
            }

            return removed.Count == 0 ? null : keep;
        }, ct);

        return removed;
    }

    /// <summary>
    /// Read-modify-write of the full keyCredentials collection with one retry on conflict.
    /// The single-object read uses $select so Graph returns the key material of existing
    /// certificates — required because PATCH replaces the whole collection.
    /// The mutator returns null to signal "no change needed".
    /// </summary>
    private async Task PatchKeyCredentialsWithRetry(
        string objectId, Func<List<KeyCredential>, List<KeyCredential>?> mutate, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            var app = await Wrap("read key credentials", () => graph.Applications[objectId].GetAsync(
                rc => rc.QueryParameters.Select = ["id", "keyCredentials"], ct));
            var updated = mutate(app!.KeyCredentials?.ToList() ?? []);
            if (updated is null)
            {
                return;
            }

            try
            {
                await Wrap("update key credentials", () => graph.Applications[objectId].PatchAsync(
                    new Application { KeyCredentials = updated }, cancellationToken: ct));
                return;
            }
            catch (RegnrollGraphException e) when (e.StatusCode == 409 && attempt == 1)
            {
                logger.LogWarning("Conflict while updating keyCredentials of {ObjectId}; retrying once.", objectId);
            }
        }
    }

    private async Task Iterate(ApplicationCollectionResponse? page, List<Application> sink, CancellationToken ct)
    {
        if (page is null)
        {
            return;
        }

        var iterator = PageIterator<Application, ApplicationCollectionResponse>.CreatePageIterator(graph, page, app =>
        {
            sink.Add(app);
            return true;
        });
        await iterator.IterateAsync(ct);
    }

    private static AppRegistrationModel ToModel(Application app) => new(
        app.Id ?? "",
        app.AppId ?? "",
        app.DisplayName ?? "",
        (app.PasswordCredentials ?? []).Select(c => new CredentialInfo(c.KeyId ?? Guid.Empty, c.DisplayName, c.StartDateTime, c.EndDateTime)).ToList(),
        (app.KeyCredentials ?? []).Select(c => new CredentialInfo(c.KeyId ?? Guid.Empty, c.DisplayName, c.StartDateTime, c.EndDateTime)).ToList());

    private Task Wrap(string operation, Func<Task> call) =>
        Wrap(operation, async () =>
        {
            await call();
            return true;
        });

    private async Task<T> Wrap<T>(string operation, Func<Task<T>> call)
    {
        try
        {
            return await call();
        }
        catch (ODataError e)
        {
            var status = e.ResponseStatusCode;
            var message = GraphErrorMapper.Map(operation, status, e.Error?.Code, e.Error?.Message, options.Value.UseTenantWideMode);
            logger.LogError(e, "Graph call failed ({Operation}): {Status} {Code}", operation, status, e.Error?.Code);
            throw new RegnrollGraphException(message, status == 0 ? 502 : status, e);
        }
    }
}

/// <summary>Pure translation of Graph failures into actionable admin guidance (unit tested directly).</summary>
public static class GraphErrorMapper
{
    public const string DocsUrl = "https://regnroll.github.io/guides/permissions/";

    public static string Map(string operation, int status, string? code, string? rawMessage, bool tenantWideMode)
    {
        return status switch
        {
            401 => $"Graph rejected the credentials while trying to {operation}. The managed identity could not authenticate — verify the identity exists and the app is running with it. See {DocsUrl}",
            403 => tenantWideMode
                ? $"Graph denied permission to {operation}. Tenant-wide mode requires the app-only permission Application.ReadWrite.All to be granted (admin consent) to the managed identity. Run infra/scripts/grant-graph-permissions.ps1 or see {DocsUrl}"
                : $"Graph denied permission to {operation}. In OwnedBy mode the managed identity needs the app-only permission Application.ReadWrite.OwnedBy (admin consent) AND must be an owner of the target app registration. Grant the permission with infra/scripts/grant-graph-permissions.ps1 and add the identity as owner, or switch to tenant-wide mode. See {DocsUrl}",
            404 => $"Graph could not find the target while trying to {operation}. The app registration may have been deleted, or in OwnedBy mode it may not be owned by the managed identity. See {DocsUrl}",
            _ => $"Graph call failed while trying to {operation}: {code ?? "unknown"} — {rawMessage ?? "no details"}.",
        };
    }

    public static string LimitedProfile(bool tenantWideMode)
    {
        var permission = tenantWideMode ? "Application.ReadWrite.All" : "Application.ReadWrite.OwnedBy";
        return $"Graph returned the app registrations without their properties (limited profile), which means the managed identity has not been granted the app-only permission {permission} (admin consent). Run infra/scripts/grant-graph-permissions.ps1, then restart the app. See {DocsUrl}";
    }
}
