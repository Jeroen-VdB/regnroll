using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Regnroll.App.Infrastructure;
using Regnroll.App.Services;

namespace Regnroll.App.Functions.Http;

public record TemplateRequest(string? Subject, string? HtmlBody);

/// <summary>Email template management: overrides apply to subsequent sends without redeploying.</summary>
public class AdminTemplates(ITemplateService templates)
{
    [Function($"{nameof(AdminTemplates)}_{nameof(List)}")]
    public async Task<IActionResult> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "api/admin/templates")] HttpRequest req,
        CancellationToken ct)
    {
        if (!AdminAuth.IsAuthorized(req))
        {
            return AdminAuth.Unauthorized();
        }

        return new OkObjectResult(await templates.ListAsync(ct));
    }

    [Function($"{nameof(AdminTemplates)}_{nameof(Save)}")]
    public async Task<IActionResult> Save(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "api/admin/templates/{key}")] HttpRequest req,
        string key,
        CancellationToken ct)
    {
        if (!AdminAuth.IsAuthorized(req))
        {
            return AdminAuth.Unauthorized();
        }

        var body = await req.ReadFromJsonAsync<TemplateRequest>(ct);
        if (string.IsNullOrWhiteSpace(body?.Subject) || string.IsNullOrWhiteSpace(body.HtmlBody))
        {
            return new BadRequestObjectResult(new { error = "invalid_request", message = "Both 'subject' and 'htmlBody' are required." });
        }

        try
        {
            await templates.SaveOverrideAsync(key, body.Subject, body.HtmlBody, ct);
        }
        catch (ArgumentException)
        {
            return new NotFoundObjectResult(new { error = "unknown_template", message = $"'{key}' is not a known template key." });
        }

        return new OkObjectResult(await templates.GetAsync(key, ct));
    }

    [Function($"{nameof(AdminTemplates)}_{nameof(Reset)}")]
    public async Task<IActionResult> Reset(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "api/admin/templates/{key}")] HttpRequest req,
        string key,
        CancellationToken ct)
    {
        if (!AdminAuth.IsAuthorized(req))
        {
            return AdminAuth.Unauthorized();
        }

        await templates.ResetAsync(key, ct);
        try
        {
            return new OkObjectResult(await templates.GetAsync(key, ct));
        }
        catch (ArgumentException)
        {
            return new NotFoundObjectResult(new { error = "unknown_template", message = $"'{key}' is not a known template key." });
        }
    }
}
