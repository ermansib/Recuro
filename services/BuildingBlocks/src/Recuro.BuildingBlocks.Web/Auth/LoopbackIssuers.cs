namespace Recuro.BuildingBlocks.Web.Auth;

/// <summary>
/// Keycloak names its issuer after the host the browser used. Locally the main portal signs in at 127.0.0.1:8080
/// (Docker's IPv6 mapping breaks localhost on some machines) while services are configured with localhost:8080,
/// so the same realm issues tokens under two names. Both are the same Keycloak, so a loopback authority accepts its
/// loopback alias too. Any other host is accepted only exactly as configured.
/// </summary>
public static class LoopbackIssuers
{
    private static readonly string[] Aliases = ["localhost", "127.0.0.1"];

    /// <summary>The configured issuers plus, for a loopback issuer, the same URL under the other loopback name.</summary>
    public static string[] For(params string?[] authorities)
    {
        var issuers = new List<string>();
        foreach (var authority in authorities.OfType<string>().Where(a => !string.IsNullOrWhiteSpace(a)))
        {
            issuers.Add(authority);
            if (Uri.TryCreate(authority, UriKind.Absolute, out var uri) && Aliases.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            {
                issuers.AddRange(Aliases.Select(host => new UriBuilder(uri) { Host = host }.Uri.ToString())
                    .Select(alias => authority.EndsWith('/') ? alias : alias.TrimEnd('/')));
            }
        }

        return [.. issuers.Distinct(StringComparer.Ordinal)];
    }
}
