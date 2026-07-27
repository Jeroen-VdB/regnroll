using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Regnroll.App.Infrastructure;

/// <summary>
/// Fail-closed admin authorization on top of App Service built-in authentication (EasyAuth).
/// The platform injects X-MS-CLIENT-PRINCIPAL (base64 JSON) for authenticated requests; external
/// callers cannot spoof it because App Service strips the header. On Azure, a missing or invalid
/// principal always means 401 — even if platform auth was accidentally disabled. Outside Azure
/// (local development, WEBSITE_SITE_NAME absent) the admin surface is open.
/// </summary>
public static class AdminAuth
{
    private const string PrincipalHeader = "X-MS-CLIENT-PRINCIPAL";

    public static bool RunningOnAzure => Environment.GetEnvironmentVariable("WEBSITE_SITE_NAME") is not null;

    public static bool IsAuthorized(HttpRequest request) =>
        GetPrincipal(request)?.Identity?.IsAuthenticated == true || !RunningOnAzure;

    public static IActionResult Unauthorized() =>
        new ObjectResult(new { error = "unauthorized", message = "Sign in via App Service authentication is required." })
        {
            StatusCode = StatusCodes.Status401Unauthorized,
        };

    public static ClaimsPrincipal? GetPrincipal(HttpRequest request)
    {
        try
        {
            if (!request.Headers.TryGetValue(PrincipalHeader, out var header) || header.Count == 0)
            {
                return null;
            }

            var payload = JsonSerializer.Deserialize<ClientPrincipalPayload>(Convert.FromBase64String(header[0]!));
            if (payload is null || string.IsNullOrEmpty(payload.AuthType))
            {
                return null;
            }

            var claims = (payload.Claims ?? []).Select(c => new Claim(c.Type ?? "", c.Value ?? ""));
            var identity = new ClaimsIdentity(claims, payload.AuthType, payload.NameType, payload.RoleType);
            return new ClaimsPrincipal(identity);
        }
        catch
        {
            // Any parse failure counts as unauthenticated — fail closed.
            return null;
        }
    }

    private sealed record ClientPrincipalPayload(
        [property: JsonPropertyName("auth_typ")] string? AuthType,
        [property: JsonPropertyName("claims")] List<ClientPrincipalClaim>? Claims,
        [property: JsonPropertyName("name_typ")] string? NameType,
        [property: JsonPropertyName("role_typ")] string? RoleType);

    private sealed record ClientPrincipalClaim(
        [property: JsonPropertyName("typ")] string? Type,
        [property: JsonPropertyName("val")] string? Value);
}
