using System.Net;
using System.Text.RegularExpressions;

namespace E_COMMERCE_FuentesFacundo.Tests;

public static class TestHelpers
{
    public static string? GetAntiforgeryToken(string html)
    {
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        return match.Success ? match.Groups[1].Value : null;
    }

    public static async Task<string> ReadAsStringAsync(this HttpResponseMessage response)
    {
        return await response.Content.ReadAsStringAsync();
    }
}
