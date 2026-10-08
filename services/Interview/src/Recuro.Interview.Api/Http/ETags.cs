using System.Globalization;
using Microsoft.Net.Http.Headers;

namespace Recuro.Interview.Api.Http;

/// <summary>Strong ETags carrying the row version, for optimistic locking on draft autosave (RCU-REQ-001).</summary>
internal static class ETags
{
    public static void Write(HttpContext http, uint version) =>
        http.Response.Headers.ETag = string.Create(CultureInfo.InvariantCulture, $"\"{version}\"");

    /// <summary>The version in If-Match, or null when the client sent none (the update is then unconditional).</summary>
    public static uint? ReadIfMatch(HttpContext http)
    {
        var raw = http.Request.Headers[HeaderNames.IfMatch].ToString();
        if (string.IsNullOrWhiteSpace(raw) || raw == "*")
        {
            return null;
        }

        var value = raw.Trim();
        if (value.StartsWith("W/", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        // An unreadable tag can't match any version: treat it as stale rather than unconditional.
        return uint.TryParse(value.Trim('"'), NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : 0;
    }
}
