using System.Net;
using System.Net.Http.Json;
using Recuro.Bgv.Application.Abstractions;

namespace Recuro.Bgv.Infrastructure.Integration;

public sealed class VendorServiceOptions
{
    public const string SectionName = "Services:Vendor";

    public Uri BaseUrl { get; set; } = new("http://localhost:5113/");

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);
}

/// <summary>The Vendor service's <c>GET /api/v1/vendors/{vendorId}/status</c> response (architecture.md).</summary>
internal sealed record VendorStatusResponse(string VendorId, string Status, bool Active, string? Name, string? Type);

/// <summary>
/// RCU-BGV-001: only active, empanelled BGV agencies can take a case. Asked live, with the caller's
/// credentials, so a de-empanelled vendor is refused at once. Unknown vendors (404) are null.
/// </summary>
internal sealed class VendorDirectoryClient(HttpClient http) : IVendorDirectory
{
    public async Task<VendorStatus?> GetAsync(string vendorId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorId);
        try
        {
            using var response = await http.GetAsync(new Uri($"api/v1/vendors/{Uri.EscapeDataString(vendorId)}/status", UriKind.Relative), ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<VendorStatusResponse>(ct)
                ?? throw new DependencyUnavailableException("The Vendor service returned an empty status.");
            return new VendorStatus(body.VendorId, body.Name ?? body.VendorId, body.Type ?? string.Empty, body.Active);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException && !ct.IsCancellationRequested)
        {
            throw new DependencyUnavailableException("The Vendor service is unreachable.", ex);
        }
    }
}
