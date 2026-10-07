using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Recuro.Candidate.Application.Abstractions;

namespace Recuro.Candidate.Infrastructure.Integration;

public sealed class VendorServiceOptions
{
    public const string SectionName = "Services:Vendor";

    /// <summary>Off until the Vendor service (wave 2) is deployed; every consultant is accepted meanwhile.</summary>
    public bool Enabled { get; set; }

    public Uri BaseUrl { get; set; } = new("http://localhost:5113/");

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(3);
}

/// <summary>Vendor's <c>GET /api/v1/vendors/{vendorId}/status</c> response (architecture.md).</summary>
internal sealed record VendorStatusResponse(string VendorId, string Status, bool Active);

/// <summary>
/// RCU-CND-005: asks the Vendor service whether a consultant is active. 404 means not active. If Vendor
/// can't be reached the consultant is accepted and the gap is logged, the same as before Vendor existed.
/// </summary>
internal sealed partial class VendorDirectoryClient(HttpClient http, ILogger<VendorDirectoryClient> logger) : IVendorDirectory
{
    public async Task<bool> IsActiveConsultantAsync(string consultantId, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(consultantId);
        try
        {
            using var response = await http.GetAsync(new Uri($"api/v1/vendors/{Uri.EscapeDataString(consultantId)}/status", UriKind.Relative), ct);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }

            response.EnsureSuccessStatusCode();
            var status = await response.Content.ReadFromJsonAsync<VendorStatusResponse>(ct);
            return status?.Active == true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            NotChecked(logger, consultantId, ex);
            return true;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Consultant {ConsultantId} accepted without a vendor check: the Vendor service did not answer")]
    private static partial void NotChecked(ILogger logger, string consultantId, Exception ex);
}
