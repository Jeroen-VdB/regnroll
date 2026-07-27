using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Regnroll.App.Infrastructure;

namespace Regnroll.App.Functions.Http;

/// <summary>The admin portal shell, served at the site root behind EasyAuth + the fail-closed principal check.</summary>
public class AdminPage
{
    [Function($"{nameof(AdminPage)}_{nameof(Index)}")]
    public IActionResult Index(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "")] HttpRequest req) =>
        AdminAuth.IsAuthorized(req) ? StaticFiles.Serve("index.html") : AdminAuth.Unauthorized();
}
