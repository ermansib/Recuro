using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Recuro.Admin.Infrastructure.Persistence.Seed;

namespace Recuro.Admin.IntegrationTests;

/// <summary>Hosts the real API on a throwaway SQLite database with the development sign-in.</summary>
public sealed class AdminApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath = Path.Combine(Path.GetTempPath(), $"recuro-admin-{Guid.NewGuid():N}.db");

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Aurora => $"tenant:{DatabaseSeeder.AuroraTenantId}";

    public static string TalentBridge => $"tenant:{DatabaseSeeder.TalentBridgeTenantId}";

    public HttpClient ClientAs(string? persona)
    {
        var client = CreateClient();
        if (persona is not null)
        {
            client.DefaultRequestHeaders.Add("X-Recuro-Persona", persona);
        }

        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Database:Provider", "Sqlite");
        builder.UseSetting("Database:SeedDemoTenants", "true");
        builder.UseSetting("ConnectionStrings:AdminDb", $"Data Source={_databasePath}");
        builder.UseSetting("Auth:Mode", "Development");
        builder.UseSetting("AccountDirectory:Mode", "Disabled");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(_databasePath);
    }
}
