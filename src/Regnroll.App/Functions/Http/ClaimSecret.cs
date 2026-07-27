using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Regnroll.App.Infrastructure;
using Regnroll.App.Services;

namespace Regnroll.App.Functions.Http;

/// <summary>
/// The ONLY operation that can consume a secret. Accepts JSON or form-encoded bodies so
/// customers can automate retrieval with a single curl command (shown on the retrieval page).
/// </summary>
public class ClaimSecret(IDeliveryService delivery)
{
    public record ClaimRequest(string? Id, string? Key);

    [Function(nameof(ClaimSecret))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/claim")] HttpRequest req,
        CancellationToken ct)
    {
        string? id, key;
        if (req.HasFormContentType)
        {
            var form = await req.ReadFormAsync(ct);
            id = form["id"];
            key = form["key"];
        }
        else
        {
            var body = await RequestJson.ReadOrNullAsync<ClaimRequest>(req, ct);
            id = body?.Id;
            key = body?.Key;
        }

        if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(key))
        {
            return new BadRequestObjectResult(new { error = "invalid_request", message = "Both 'id' and 'key' are required." });
        }

        var result = await delivery.ClaimAsync(id, key, ct);
        return result.Status switch
        {
            ClaimStatus.Success => new OkObjectResult(new
            {
                secret = result.Secret,
                clientId = result.ClientId,
                newSecretExpiresAt = result.NewSecretExpiresAt,
            }),
            ClaimStatus.InvalidKey => new BadRequestObjectResult(new
            {
                error = "invalid_key",
                message = "The decryption key is not valid for this link. The secret has NOT been consumed — check the full URL from the email.",
            }),
            ClaimStatus.Expired => new ObjectResult(new
            {
                error = "expired",
                message = "This link has expired. Contact your IT support to have a new one issued.",
            })
            { StatusCode = StatusCodes.Status410Gone },
            _ => new ObjectResult(new
            {
                error = "gone",
                message = "This secret is no longer available — it was already retrieved or the link was superseded. Contact your IT support if you did not retrieve it.",
            })
            { StatusCode = StatusCodes.Status410Gone },
        };
    }
}
