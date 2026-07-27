using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Regnroll.App.Infrastructure;

namespace Regnroll.App.Functions.Http;

/// <summary>
/// Serves the admin portal shell at the site root, behind EasyAuth + the fail-closed principal check.
/// Route notes (hard-won):
///  - A literal "" route is rejected by the host ("conflicts with built in routes").
///  - The host matches custom routes in alphabetical FUNCTION NAME order, first match wins — a
///    catch-all {*path} on an early-sorting name swallows /api/* and /s/*. Hence a single-segment
///    optional route, which structurally cannot shadow any multi-segment route, on a late-sorting name.
///  - AzureWebJobsDisableHomepage=true keeps the platform homepage from occupying "/".
/// </summary>
public class StaticPage
{
    [Function($"{nameof(StaticPage)}_{nameof(Index)}")]
    public IActionResult Index(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "{page?}")] HttpRequest req, string? page)
    {
        if (!AdminAuth.IsAuthorized(req))
        {
            return AdminAuth.Unauthorized();
        }

        return string.IsNullOrEmpty(page) || page == "index.html"
            ? StaticFiles.Serve("index.html")
            : new NotFoundResult();
    }
}
