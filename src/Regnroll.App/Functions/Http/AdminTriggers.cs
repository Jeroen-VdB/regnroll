using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Regnroll.App.Infrastructure;
using Regnroll.App.Models;
using Regnroll.App.Services;

namespace Regnroll.App.Functions.Http;

/// <summary>Manual flow triggers. A manual trigger supersedes any pending link of the same type.</summary>
public class AdminTriggers(
    IMetadataStore metadataStore,
    IGraphAppService graphService,
    IDeliveryService delivery,
    ILifecycleService lifecycle)
{
    [Function($"{nameof(AdminTriggers)}_{nameof(NewSecret)}")]
    public Task<IActionResult> NewSecret(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/admin/apps/{clientId}/secret")] HttpRequest req,
        string clientId,
        CancellationToken ct) => Trigger(req, clientId, CredentialTypes.Secret, ct);

    [Function($"{nameof(AdminTriggers)}_{nameof(NewCertificate)}")]
    public Task<IActionResult> NewCertificate(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/admin/apps/{clientId}/certificate")] HttpRequest req,
        string clientId,
        CancellationToken ct) => Trigger(req, clientId, CredentialTypes.Certificate, ct);

    /// <summary>Runs the daily lifecycle scan on demand — used for verification and after settings changes.</summary>
    [Function($"{nameof(AdminTriggers)}_{nameof(RunScan)}")]
    public async Task<IActionResult> RunScan(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/admin/scan")] HttpRequest req,
        CancellationToken ct)
    {
        if (!AdminAuth.IsAuthorized(req))
        {
            return AdminAuth.Unauthorized();
        }

        var summary = await lifecycle.RunAsync(ct);
        return new OkObjectResult(summary);
    }

    private async Task<IActionResult> Trigger(HttpRequest req, string clientId, string type, CancellationToken ct)
    {
        if (!AdminAuth.IsAuthorized(req))
        {
            return AdminAuth.Unauthorized();
        }

        var entity = await metadataStore.GetAsync(clientId, ct);
        if (entity is null)
        {
            return new NotFoundObjectResult(new { error = "not_linked", message = "Link this app registration before triggering credential flows." });
        }

        try
        {
            var graphApp = await graphService.GetByClientIdAsync(clientId, ct);
            var oldExpiry = graphApp.LatestExpiring(type)?.EndDateTime;
            var link = type == CredentialTypes.Secret
                ? await delivery.StartSecretDeliveryAsync(entity, oldExpiry, ct)
                : await delivery.StartCertificateUploadAsync(entity, oldExpiry, ct);
            return new OkObjectResult(new { triggered = type, expiresAt = link.ExpiresAt });
        }
        catch (RegnrollGraphException e)
        {
            return new ObjectResult(new { error = "graph_error", message = e.Message })
            { StatusCode = e.StatusCode is >= 400 and < 600 ? e.StatusCode : 502 };
        }
        catch (DeliveryException e)
        {
            return new ObjectResult(new { error = "delivery_failed", message = e.Message }) { StatusCode = StatusCodes.Status502BadGateway };
        }
    }
}
