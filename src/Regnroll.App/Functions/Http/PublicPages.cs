using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Regnroll.App.Infrastructure;

namespace Regnroll.App.Functions.Http;

/// <summary>
/// Anonymous customer-facing pages and assets. GET is always harmless here:
/// it serves only the static page shell and never touches link state.
/// </summary>
public class PublicPages
{
    [Function($"{nameof(PublicPages)}_{nameof(SecretPage)}")]
    public IActionResult SecretPage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "s/{id}")] HttpRequest req, string id) =>
        StaticFiles.Serve("s.html");

    [Function($"{nameof(PublicPages)}_{nameof(CertificatePage)}")]
    public IActionResult CertificatePage(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "c/{token}")] HttpRequest req, string token) =>
        StaticFiles.Serve("c.html");

    [Function($"{nameof(PublicPages)}_{nameof(Assets)}")]
    public IActionResult Assets(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "assets/{*path}")] HttpRequest req, string path) =>
        StaticFiles.Serve(Path.Combine("assets", path ?? ""));

    [Function($"{nameof(PublicPages)}_{nameof(Health)}")]
    public IActionResult Health(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/health")] HttpRequest req) =>
        new OkObjectResult(new { status = "ok" });
}
