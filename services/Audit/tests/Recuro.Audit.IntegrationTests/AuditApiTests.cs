using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Recuro.Audit.Infrastructure.Jobs;
using Recuro.Audit.Infrastructure.Persistence;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Messaging;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.BuildingBlocks.Web.Middleware;

namespace Recuro.Audit.IntegrationTests;

public sealed class AuditApiTests(AuditApiFactory api) : IClassFixture<AuditApiFactory>
{
    private static object Entry(string entity, string action = "STATE_CHANGE") => new
    {
        entity,
        action,
        before = "Draft",
        after = "PendingApproval",
        configVersion = "2026.08",
        actorId = "u-42",
        actorName = "A. Sharma",
        actorRole = RecuroRoles.HrTa,
    };

    [Fact]
    public async Task A_service_records_an_entry_and_HR_staff_read_it_in_the_frontend_shape()
    {
        var entity = $"Requisition/REQ-{Guid.NewGuid():N}";
        var service = api.ClientFor(AuditApiFactory.TenantA, RecuroRoles.Service, "svc-requisition");

        var created = await service.PostAsJsonAsync("/api/v1/audit/ingest", Entry(entity));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var list = await api.ClientFor(AuditApiFactory.TenantA, RecuroRoles.HrHead)
            .GetFromJsonAsync<JsonElement>($"/api/v1/audit?entity={Uri.EscapeDataString(entity)}");
        var item = Assert.Single(list.EnumerateArray());

        // Exactly the frontend AuditEvent fields (frontend/src/domain/types.ts), no more.
        Assert.Equal(
            ["action", "actor", "after", "at", "before", "configVersion", "entity", "id", "role"],
            item.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("A. Sharma", item.GetProperty("actor").GetString());
        Assert.Equal("hrta", item.GetProperty("role").GetString());
        Assert.Equal("PendingApproval", item.GetProperty("after").GetString());
    }

    [Fact]
    public async Task Another_tenant_never_sees_the_entry()
    {
        var entity = $"Offer/OFF-{Guid.NewGuid():N}";
        await api.ClientFor(AuditApiFactory.TenantA, RecuroRoles.Service).PostAsJsonAsync("/api/v1/audit/ingest", Entry(entity));

        var list = await api.ClientFor(AuditApiFactory.TenantB, RecuroRoles.HrHead)
            .GetFromJsonAsync<JsonElement>($"/api/v1/audit?entity={Uri.EscapeDataString(entity)}");

        Assert.Empty(list.EnumerateArray());
    }

    [Fact]
    public async Task People_cannot_write_to_the_trail_directly()
    {
        var response = await api.ClientFor(AuditApiFactory.TenantA, RecuroRoles.HrTa)
            .PostAsJsonAsync("/api/v1/audit/ingest", Entry("Requisition/REQ-1"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Candidates_cannot_read_the_trail()
    {
        var response = await api.ClientFor(AuditApiFactory.TenantA, RecuroRoles.Candidate).GetAsync("/api/v1/audit");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Anonymous_callers_get_401()
    {
        var response = await api.CreateClient().GetAsync("/api/v1/audit");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_input_returns_400_with_field_errors()
    {
        var response = await api.ClientFor(AuditApiFactory.TenantA, RecuroRoles.Service)
            .PostAsJsonAsync("/api/v1/audit/ingest", new { entity = "", action = "X" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("validation_failed", problem.GetProperty("code").GetString());
        Assert.Equal("entity", problem.GetProperty("errors")[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task Replaying_an_idempotency_key_returns_the_original_response_without_a_second_entry()
    {
        var entity = $"Requisition/REQ-{Guid.NewGuid():N}";
        var service = api.ClientFor(AuditApiFactory.TenantA, RecuroRoles.Service);
        service.DefaultRequestHeaders.Add(IdempotencyMiddleware.KeyHeader, Guid.NewGuid().ToString());

        var first = await service.PostAsJsonAsync("/api/v1/audit/ingest", Entry(entity));
        var second = await service.PostAsJsonAsync("/api/v1/audit/ingest", Entry(entity));

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await second.Content.ReadAsStringAsync());
        Assert.True(second.Headers.Contains(IdempotencyMiddleware.ReplayedHeader));
        var list = await api.ClientFor(AuditApiFactory.TenantA, RecuroRoles.HrTa)
            .GetFromJsonAsync<JsonElement>($"/api/v1/audit?entity={Uri.EscapeDataString(entity)}");
        Assert.Single(list.EnumerateArray());
    }

    [Fact]
    public async Task Responses_carry_correlation_ids()
    {
        var client = api.ClientFor(AuditApiFactory.TenantA, RecuroRoles.HrTa);
        client.DefaultRequestHeaders.Add(CorrelationMiddleware.CorrelationIdHeader, "saga-123");

        var response = await client.GetAsync("/api/v1/audit");

        Assert.Equal("saga-123", response.Headers.GetValues(CorrelationMiddleware.CorrelationIdHeader).Single());
        Assert.False(string.IsNullOrEmpty(response.Headers.GetValues(CorrelationMiddleware.RequestIdHeader).Single()));
    }

    [Fact]
    public async Task Events_from_the_bus_are_mirrored_once_even_if_redelivered()
    {
        var subject = $"Requisition/REQ-{Guid.NewGuid():N}";
        var cloudEvent = new CloudEvent
        {
            Id = Guid.CreateVersion7(),
            Source = "/services/requisition",
            Type = "recruitment.mrf.submitted.v1",
            Subject = subject,
            Time = DateTimeOffset.UtcNow,
            TenantId = AuditApiFactory.TenantA,
            CorrelationId = "corr-9",
            ActorId = "u-7",
            ActorName = "R. Iyer",
            ActorRole = RecuroRoles.HrTa,
            Data = JsonSerializer.SerializeToElement(new { reqId = "REQ-2026-0001", configVersionId = "2026.08" }),
        };
        var processor = api.Services.GetRequiredService<IntegrationEventProcessor>();

        await processor.ProcessAsync(cloudEvent, CancellationToken.None);
        await processor.ProcessAsync(cloudEvent, CancellationToken.None);

        var list = await api.ClientFor(AuditApiFactory.TenantA, RecuroRoles.HrTa)
            .GetFromJsonAsync<JsonElement>($"/api/v1/audit?entity={Uri.EscapeDataString(subject)}");
        var item = Assert.Single(list.EnumerateArray());
        Assert.Equal("recruitment.mrf.submitted.v1", item.GetProperty("action").GetString());
        Assert.Equal("R. Iyer", item.GetProperty("actor").GetString());
        Assert.Equal("2026.08", item.GetProperty("configVersion").GetString());
    }

    [Fact]
    public async Task Concurrent_writes_keep_one_unbroken_chain()
    {
        var service = api.ClientFor(AuditApiFactory.TenantB, RecuroRoles.Service);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(i => service.PostAsJsonAsync("/api/v1/audit/ingest", Entry($"Bulk/{i}"))));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));
        var verification = await api.ClientFor(AuditApiFactory.TenantB, RecuroRoles.HrHead)
            .GetFromJsonAsync<JsonElement>("/api/v1/audit/verify");
        Assert.True(verification.GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task The_database_refuses_updates_and_deletes()
    {
        await api.ClientFor(AuditApiFactory.TenantA, RecuroRoles.Service).PostAsJsonAsync("/api/v1/audit/ingest", Entry("Tamper/1"));
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();

        var update = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("UPDATE audit_entries SET reason = 'edited'"));
        var delete = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlRawAsync("DELETE FROM audit_entries"));

        Assert.Contains("append-only", update.MessageText, StringComparison.Ordinal);
        Assert.Contains("append-only", delete.MessageText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_seal_job_checkpoints_each_intact_chain()
    {
        await api.ClientFor(AuditApiFactory.TenantA, RecuroRoles.Service).PostAsJsonAsync("/api/v1/audit/ingest", Entry("Seal/1"));
        var job = ActivatorUtilities.CreateInstance<AuditSealJob>(api.Services);

        Assert.True(await job.SealAllTenantsAsync(CancellationToken.None));

        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(AuditApiFactory.TenantA);
        var db = scope.ServiceProvider.GetRequiredService<AuditDbContext>();
        var lastSequence = await db.Entries.MaxAsync(e => e.Sequence);
        var seal = await db.Seals.OrderByDescending(s => s.UpToSequence).FirstAsync();
        Assert.Equal(lastSequence, seal.UpToSequence);
    }
}
