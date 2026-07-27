using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace Regnroll.App.Infrastructure;

public static class RequestJson
{
    /// <summary>Reads a JSON body, returning null (instead of throwing) on malformed input so endpoints answer 400, not 500.</summary>
    public static async Task<T?> ReadOrNullAsync<T>(HttpRequest request, CancellationToken ct) where T : class
    {
        try
        {
            return await request.ReadFromJsonAsync<T>(ct);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
