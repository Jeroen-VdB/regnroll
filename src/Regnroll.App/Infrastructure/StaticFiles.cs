using Microsoft.AspNetCore.Mvc;

namespace Regnroll.App.Infrastructure;

/// <summary>Serves the hand-written static UI from the wwwroot folder next to the app binaries.</summary>
public static class StaticFiles
{
    private static readonly string Root = Path.Combine(AppContext.BaseDirectory, "wwwroot");

    public static IActionResult Serve(string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(Root, relativePath));
        if (!full.StartsWith(Root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            return new NotFoundResult();
        }

        return new PhysicalFileResult(full, ContentType(full));
    }

    private static string ContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".png" => "image/png",
        ".svg" => "image/svg+xml",
        ".ico" => "image/x-icon",
        _ => "application/octet-stream",
    };
}
