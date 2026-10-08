using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.BuildingBlocks.Infrastructure.Messaging;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Candidate.Infrastructure.Jobs;
using Recuro.Candidate.Infrastructure.Persistence;

namespace Recuro.Candidate.IntegrationTests;

public sealed class CandidateApiTests(CandidateApiFactory api) : IClassFixture<CandidateApiFactory>
{
    private static object NewCandidate(string? email = null, string source = "Portal", bool privacy = true) => new
    {
        name = "Rahul Mehta",
        email = email ?? $"rahul.{Guid.NewGuid():N}@email.example",
        phone = $"+91 9{Random.Shared.Next(100_000_000, 999_999_999)}",
        experienceYears = 8,
        summary = "8 yrs · BFSI Credit",
        currentCtc = 19,
        expectedCtc = 21,
        noticeDays = 30,
        source,
        consents = privacy ? new[] { new { type = "DataPrivacy", textVersion = "v1" } } : [],
    };

    private async Task<JsonElement> CreateAsync(Guid tenant, object body)
    {
        var response = await api.ClientFor(tenant, RecuroRoles.HrTa).PostAsJsonAsync("/api/v1/candidates", body);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task HR_TA_creates_a_candidate_and_reads_it_back_in_the_frontend_shape()
    {
        var created = await CreateAsync(CandidateApiFactory.TenantA, NewCandidate(source: "Walk-in"));

        var read = await api.ClientFor(CandidateApiFactory.TenantA, RecuroRoles.HrHead)
            .GetFromJsonAsync<JsonElement>($"/api/v1/candidates/{created.GetProperty("id").GetString()}");

        // Exactly the frontend Candidate fields (frontend/src/domain/types.ts); sourceRef is optional and absent.
        Assert.Equal(
            ["consents", "currentCtc", "email", "expectedCtc", "experienceYears", "id", "name", "noticeDays", "phone", "source", "summary"],
            read.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal("Rahul Mehta", read.GetProperty("name").GetString());
        Assert.Equal("Walk-in", read.GetProperty("source").GetString());
        Assert.Equal(19, read.GetProperty("currentCtc").GetDecimal());
        var consent = Assert.Single(read.GetProperty("consents").EnumerateArray());
        Assert.Equal(["at", "textVersion", "type"], consent.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Personal_data_is_encrypted_in_the_database()
    {
        var email = $"secret.{Guid.NewGuid():N}@email.example";
        var created = await CreateAsync(CandidateApiFactory.TenantA, NewCandidate(email));
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CandidateDbContext>();

        var raw = await db.Database
            .SqlQuery<string>($"SELECT email AS \"Value\" FROM candidates WHERE id = {Guid.Parse(created.GetProperty("id").GetString()!)}")
            .SingleAsync();

        Assert.DoesNotContain("secret", raw, StringComparison.Ordinal);
        Assert.StartsWith("test:", raw, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MD_CEO_reads_candidates_with_CTC_masked()
    {
        var created = await CreateAsync(CandidateApiFactory.TenantA, NewCandidate());

        var read = await api.ClientFor(CandidateApiFactory.TenantA, RecuroRoles.MdCeo)
            .GetFromJsonAsync<JsonElement>($"/api/v1/candidates/{created.GetProperty("id").GetString()}");

        Assert.Equal(JsonValueKind.Null, read.GetProperty("currentCtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, read.GetProperty("expectedCtc").ValueKind);
        Assert.StartsWith("••••", read.GetProperty("email").GetString(), StringComparison.Ordinal);
        Assert.Equal("Rahul Mehta", read.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Missing_privacy_consent_is_a_400_with_field_errors()
    {
        var response = await api.ClientFor(CandidateApiFactory.TenantA, RecuroRoles.HrTa)
            .PostAsJsonAsync("/api/v1/candidates", NewCandidate(privacy: false));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("consents", problem.GetProperty("errors")[0].GetProperty("field").GetString());
    }

    [Fact]
    public async Task Invalid_input_returns_400_with_camelCase_fields()
    {
        var response = await api.ClientFor(CandidateApiFactory.TenantA, RecuroRoles.HrTa)
            .PostAsJsonAsync("/api/v1/candidates", new { name = "", email = "not-an-email", source = "Fax", consents = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("errors").EnumerateArray()
            .Select(e => e.GetProperty("field").GetString()).ToList();
        Assert.Contains("name", fields);
        Assert.Contains("email", fields);
        Assert.Contains("source", fields);
    }

    [Fact]
    public async Task The_same_email_in_another_spelling_is_a_duplicate()
    {
        var email = $"dupe.{Guid.NewGuid():N}@email.example";
        var first = await CreateAsync(CandidateApiFactory.TenantA, NewCandidate(email));

        var response = await api.ClientFor(CandidateApiFactory.TenantA, RecuroRoles.HrTa)
            .PostAsJsonAsync("/api/v1/candidates", NewCandidate(email.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("duplicate_candidate", problem.GetProperty("code").GetString());
        Assert.Contains(first.GetProperty("id").GetString()!, problem.GetProperty("title").GetString(), StringComparison.Ordinal);
        Assert.Equal(Id(first), problem.GetProperty("existingId").GetString());
    }

    [Fact]
    public async Task Another_tenant_never_sees_the_candidate_and_may_register_the_same_person()
    {
        var email = $"tenant.{Guid.NewGuid():N}@email.example";
        var created = await CreateAsync(CandidateApiFactory.TenantA, NewCandidate(email));

        var read = await api.ClientFor(CandidateApiFactory.TenantB, RecuroRoles.HrHead)
            .GetAsync($"/api/v1/candidates/{created.GetProperty("id").GetString()}");
        var batch = await api.ClientFor(CandidateApiFactory.TenantB, RecuroRoles.HrHead)
            .GetFromJsonAsync<JsonElement>($"/api/v1/candidates?ids={created.GetProperty("id").GetString()}");

        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        Assert.Empty(batch.EnumerateArray());
        await CreateAsync(CandidateApiFactory.TenantB, NewCandidate(email));
    }

    [Theory]
    [InlineData(RecuroRoles.MdCeo)]
    [InlineData(RecuroRoles.Employee)]
    [InlineData(RecuroRoles.Candidate)]
    public async Task Only_HR_TA_and_services_create_candidates(string role)
    {
        var response = await api.ClientFor(CandidateApiFactory.TenantA, role).PostAsJsonAsync("/api/v1/candidates", NewCandidate());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employees_cannot_read_candidates_and_anonymous_callers_get_401()
    {
        var created = await CreateAsync(CandidateApiFactory.TenantA, NewCandidate());
        var path = $"/api/v1/candidates/{created.GetProperty("id").GetString()}";

        Assert.Equal(HttpStatusCode.Forbidden, (await api.ClientFor(CandidateApiFactory.TenantA, RecuroRoles.Employee).GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await api.CreateClient().GetAsync(path)).StatusCode);
    }

    [Fact]
    public async Task Batch_read_returns_the_known_candidates()
    {
        var a = await CreateAsync(CandidateApiFactory.TenantA, NewCandidate());
        var b = await CreateAsync(CandidateApiFactory.TenantA, NewCandidate());

        var batch = await api.ClientFor(CandidateApiFactory.TenantA, RecuroRoles.HrHead)
            .GetFromJsonAsync<JsonElement>($"/api/v1/candidates?ids={a.GetProperty("id").GetString()},{b.GetProperty("id").GetString()},{Guid.NewGuid()}");

        Assert.Equal(2, batch.GetArrayLength());
    }

    [Fact]
    public async Task A_CV_is_stored_encrypted_and_downloads_intact()
    {
        var created = await CreateAsync(CandidateApiFactory.TenantA, NewCandidate());
        var path = $"/api/v1/candidates/{created.GetProperty("id").GetString()}/resume";
        var pdf = "%PDF-1.7 Rahul Mehta CV"u8.ToArray();
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(pdf);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/pdf");
        form.Add(file, "file", "rahul-cv.pdf");

        var upload = await api.ClientFor(CandidateApiFactory.TenantA, RecuroRoles.HrTa).PutAsync(path, form);
        var download = await api.ClientFor(CandidateApiFactory.TenantA, RecuroRoles.HrHead).GetAsync(path);
        var mdCeo = await api.ClientFor(CandidateApiFactory.TenantA, RecuroRoles.MdCeo).GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Equal(pdf, await download.Content.ReadAsByteArrayAsync());
        Assert.Equal(HttpStatusCode.Forbidden, mdCeo.StatusCode);
        var stored = Directory.EnumerateFiles(api.ResumeRoot, "*", SearchOption.AllDirectories).Select(File.ReadAllBytes).ToList();
        Assert.DoesNotContain(stored, bytes => bytes.AsSpan().IndexOf("Rahul"u8) >= 0);
    }

    [Fact]
    public async Task Only_pdf_or_word_files_are_accepted()
    {
        var created = await CreateAsync(CandidateApiFactory.TenantA, NewCandidate());
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent([1, 2, 3]);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/x-msdownload");
        form.Add(file, "file", "cv.exe");

        var upload = await api.ClientFor(CandidateApiFactory.TenantA, RecuroRoles.HrTa)
            .PutAsync($"/api/v1/candidates/{created.GetProperty("id").GetString()}/resume", form);

        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
    }

    [Fact]
    public async Task Unsuccessful_candidates_past_retention_are_anonymised_and_legal_holds_are_skipped()
    {
        var purgeTenant = Guid.NewGuid();
        var expired = await CreateAsync(purgeTenant, NewCandidate());
        var held = await CreateAsync(purgeTenant, NewCandidate());
        var active = await CreateAsync(purgeTenant, NewCandidate());
        var past = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1).ToString("O", System.Globalization.CultureInfo.InvariantCulture);
        await RejectAsync(purgeTenant, expired, "APP-1", past);
        await RejectAsync(purgeTenant, held, "APP-2", past);
        await ProcessAsync(purgeTenant, EventTypes.Pipeline.ApplicationCreated, new { appId = "APP-3", candidateId = Id(active) });
        var holdResponse = await api.ClientFor(purgeTenant, RecuroRoles.HrHead)
            .PutAsJsonAsync($"/api/v1/candidates/{Id(held)}/legal-hold", new { onHold = true, reason = "Grievance G-12" });
        Assert.Equal(HttpStatusCode.NoContent, holdResponse.StatusCode);

        var dryRun = await api.ClientFor(purgeTenant, RecuroRoles.HrHead)
            .PostAsJsonAsync("/api/v1/candidates/retention/purge", new { dryRun = true });
        var report = await dryRun.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal([Id(expired)], report.GetProperty("candidateIds").EnumerateArray().Select(e => e.GetString()));

        var job = ActivatorUtilities.CreateInstance<RetentionPurgeJob>(api.Services);
        Assert.True(await job.PurgeAllTenantsAsync(dryRun: false, CancellationToken.None));

        var hr = api.ClientFor(purgeTenant, RecuroRoles.HrHead);
        var purged = await hr.GetFromJsonAsync<JsonElement>($"/api/v1/candidates/{Id(expired)}");
        Assert.Equal(string.Empty, purged.GetProperty("name").GetString());
        Assert.Equal(string.Empty, purged.GetProperty("email").GetString());
        Assert.Empty(purged.GetProperty("consents").EnumerateArray());
        Assert.Equal("Rahul Mehta", (await hr.GetFromJsonAsync<JsonElement>($"/api/v1/candidates/{Id(held)}")).GetProperty("name").GetString());
        Assert.Equal("Rahul Mehta", (await hr.GetFromJsonAsync<JsonElement>($"/api/v1/candidates/{Id(active)}")).GetProperty("name").GetString());

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CandidateDbContext>();
        var types = await db.OutboxMessages.Where(m => m.TenantId == purgeTenant).Select(m => m.Type).ToListAsync();
        Assert.Single(types, t => t == EventTypes.Candidate.Purged);
        Assert.Equal(3, types.Count(t => t == EventTypes.Candidate.Created));
    }

    [Fact]
    public async Task Only_HR_Head_governs_retention()
    {
        var response = await api.ClientFor(CandidateApiFactory.TenantA, RecuroRoles.HrTa)
            .PostAsJsonAsync("/api/v1/candidates/retention/purge", new { dryRun = true });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_service_tombstones_a_candidate_from_a_failed_intake_and_a_retry_is_harmless()
    {
        var tenant = Guid.NewGuid();
        var created = await CreateAsync(tenant, NewCandidate());
        var service = api.ClientFor(tenant, RecuroRoles.Service);

        var first = await service.PostAsync($"/api/v1/candidates/{Id(created)}/tombstone", null);
        var again = await service.PostAsync($"/api/v1/candidates/{Id(created)}/tombstone", null);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
        var read = await api.ClientFor(tenant, RecuroRoles.HrHead).GetFromJsonAsync<JsonElement>($"/api/v1/candidates/{Id(created)}");
        Assert.Equal(string.Empty, read.GetProperty("name").GetString());
        Assert.Equal(string.Empty, read.GetProperty("email").GetString());

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<CandidateDbContext>();
        Assert.Single(await db.OutboxMessages.Where(m => m.TenantId == tenant && m.Type == EventTypes.Candidate.Purged).ToListAsync());
    }

    [Fact]
    public async Task Tombstone_refuses_a_candidate_on_legal_hold_or_in_an_application()
    {
        var tenant = Guid.NewGuid();
        var held = await CreateAsync(tenant, NewCandidate());
        var applied = await CreateAsync(tenant, NewCandidate());
        await api.ClientFor(tenant, RecuroRoles.HrHead)
            .PutAsJsonAsync($"/api/v1/candidates/{Id(held)}/legal-hold", new { onHold = true, reason = "Grievance G-12" });
        await ProcessAsync(tenant, EventTypes.Pipeline.ApplicationCreated, new { appId = "APP-9", candidateId = Id(applied) });
        var service = api.ClientFor(tenant, RecuroRoles.Service);

        var onHold = await service.PostAsync($"/api/v1/candidates/{Id(held)}/tombstone", null);
        var inUse = await service.PostAsync($"/api/v1/candidates/{Id(applied)}/tombstone", null);
        var missing = await service.PostAsync($"/api/v1/candidates/{Guid.NewGuid()}/tombstone", null);

        Assert.Equal(HttpStatusCode.Conflict, onHold.StatusCode);
        Assert.Equal("legal_hold", (await onHold.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.Conflict, inUse.StatusCode);
        Assert.Equal("candidate_in_use", (await inUse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Theory]
    [InlineData(RecuroRoles.HrTa)]
    [InlineData(RecuroRoles.HrHead)]
    public async Task Only_services_tombstone_candidates(string role)
    {
        var created = await CreateAsync(CandidateApiFactory.TenantA, NewCandidate());

        var response = await api.ClientFor(CandidateApiFactory.TenantA, role).PostAsync($"/api/v1/candidates/{Id(created)}/tombstone", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Pipeline_events_are_processed_once_even_if_redelivered()
    {
        var created = await CreateAsync(CandidateApiFactory.TenantA, NewCandidate());
        var cloudEvent = Event(CandidateApiFactory.TenantA, EventTypes.Pipeline.ApplicationCreated, new { appId = "APP-77", candidateId = Id(created) });
        var processor = api.Services.GetRequiredService<IntegrationEventProcessor>();

        await processor.ProcessAsync(cloudEvent, CancellationToken.None);
        await processor.ProcessAsync(cloudEvent, CancellationToken.None);

        await using var scope = api.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ScopeContext>().SetTenant(CandidateApiFactory.TenantA);
        var db = scope.ServiceProvider.GetRequiredService<CandidateDbContext>();
        var candidate = await db.Candidates.SingleAsync(c => c.Id == Guid.Parse(Id(created)));
        Assert.Single(candidate.Applications);
        Assert.Equal(1, await db.InboxMessages.CountAsync(m => m.EventId == cloudEvent.Id));
    }

    [Fact]
    public async Task Masking_follows_the_Identity_map_and_fails_closed_for_unmapped_roles()
    {
        var created = await CreateAsync(CandidateApiFactory.TenantA, NewCandidate());
        var tenant = Guid.NewGuid();
        api.Identity.SetMap("hrhead", new(StringComparer.Ordinal) { ["phone"] = "hash", ["summary"] = "partial" });
        var other = await CreateAsync(tenant, NewCandidate());

        var hashed = await api.ClientFor(tenant, RecuroRoles.HrHead).GetFromJsonAsync<JsonElement>($"/api/v1/candidates/{Id(other)}");
        api.Identity.SetMap("hrhead", []);

        Assert.Matches("^[0-9a-f]{16}$", hashed.GetProperty("phone").GetString()!);
        Assert.Equal("••••edit", hashed.GetProperty("summary").GetString());
        Assert.Equal(19, hashed.GetProperty("currentCtc").GetDecimal());

        // A service token has no persona map: Identity answers 404 and every sensitive field is hidden.
        var service = await api.ClientFor(CandidateApiFactory.TenantA, RecuroRoles.Service).GetFromJsonAsync<JsonElement>($"/api/v1/candidates/{Id(created)}");
        Assert.Equal(string.Empty, service.GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Null, service.GetProperty("currentCtc").ValueKind);
    }

    [Fact]
    public async Task An_Identity_outage_falls_back_to_the_local_table()
    {
        var tenant = Guid.NewGuid();
        var created = await CreateAsync(tenant, NewCandidate());
        api.Identity.Down = true;
        try
        {
            var read = await api.ClientFor(tenant, RecuroRoles.MdCeo).GetFromJsonAsync<JsonElement>($"/api/v1/candidates/{Id(created)}");

            Assert.Equal(JsonValueKind.Null, read.GetProperty("currentCtc").ValueKind);
            Assert.StartsWith("••••", read.GetProperty("phone").GetString(), StringComparison.Ordinal);
        }
        finally
        {
            api.Identity.Down = false;
        }
    }

    private static string Id(JsonElement candidate) => candidate.GetProperty("id").GetString()!;

    private Task RejectAsync(Guid tenant, JsonElement candidate, string appId, string retainUntil) =>
        ProcessAsync(tenant, EventTypes.Pipeline.FinalRejected, new { appId, candidateId = Id(candidate), reason = "Skills gap", retainUntil });

    private Task ProcessAsync(Guid tenant, string type, object data) =>
        api.Services.GetRequiredService<IntegrationEventProcessor>().ProcessAsync(Event(tenant, type, data), CancellationToken.None);

    private static CloudEvent Event(Guid tenant, string type, object data) => new()
    {
        Id = Guid.CreateVersion7(),
        Source = "/services/pipeline",
        Type = type,
        Subject = "Application/x",
        Time = DateTimeOffset.UtcNow,
        TenantId = tenant,
        ActorId = "u-7",
        ActorName = "R. Iyer",
        ActorRole = RecuroRoles.HrTa,
        Data = JsonSerializer.SerializeToElement(data, EventJson.Options),
    };
}
