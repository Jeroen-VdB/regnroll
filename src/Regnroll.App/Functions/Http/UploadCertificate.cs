using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Regnroll.App.Infrastructure;
using Regnroll.App.Services;

namespace Regnroll.App.Functions.Http;

/// <summary>Anonymous certificate upload endpoint. Only the public certificate part is ever accepted.</summary>
public class UploadCertificate(IDeliveryService delivery, ILogger<UploadCertificate> logger)
{
    private const long MaxContentLength = 256 * 1024;

    public record UploadRequest(string? Token, string? Certificate);

    [Function(nameof(UploadCertificate))]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "api/upload")] HttpRequest req,
        CancellationToken ct)
    {
        if (req.ContentLength > MaxContentLength)
        {
            return new ObjectResult(new { error = "too_large", message = "Certificate upload exceeds the size limit." })
            { StatusCode = StatusCodes.Status413PayloadTooLarge };
        }

        string? token, certificate;
        if (req.HasFormContentType)
        {
            var form = await req.ReadFormAsync(ct);
            token = form["token"];
            certificate = form["certificate"];
        }
        else
        {
            var body = await RequestJson.ReadOrNullAsync<UploadRequest>(req, ct);
            token = body?.Token;
            certificate = body?.Certificate;
        }

        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(certificate))
        {
            return new BadRequestObjectResult(new { error = "invalid_request", message = "Both 'token' and 'certificate' are required." });
        }

        UploadResult result;
        try
        {
            result = await delivery.UploadAsync(token, certificate, ct);
        }
        catch (Exception e) when (e is RegnrollGraphException or DeliveryException)
        {
            // Public endpoint: never leak tenant/permission internals to customers.
            logger.LogError(e, "Certificate upload failed server-side.");
            return new ObjectResult(new
            {
                error = "server_error",
                message = "The certificate could not be applied due to a server-side problem. The upload link is still valid — try again later or contact your IT support.",
            })
            { StatusCode = StatusCodes.Status502BadGateway };
        }

        return result.Status switch
        {
            UploadStatus.Success => new OkObjectResult(new { thumbprint = result.Thumbprint, notAfter = result.NotAfter }),
            UploadStatus.InvalidCertificate => new BadRequestObjectResult(new { error = "invalid_certificate", message = result.Error }),
            UploadStatus.Expired => new ObjectResult(new
            {
                error = "expired",
                message = "This upload link has expired. Contact your IT support to have a new one issued.",
            })
            { StatusCode = StatusCodes.Status410Gone },
            _ => new ObjectResult(new
            {
                error = "gone",
                message = "This upload link is no longer valid — it was already used or superseded. Contact your IT support.",
            })
            { StatusCode = StatusCodes.Status410Gone },
        };
    }
}
