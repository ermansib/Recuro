using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Infrastructure.Messaging;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.Offer.Infrastructure.Persistence;

namespace Recuro.Offer.IntegrationTests;

public sealed class OfferApiTests(OfferApiFactory api) : IClassFixture<OfferApiFactory>
{
    private const string Offers = "/api/v1/offers";

    private HttpClient HrTa(Guid? tenant = null) => api.ClientFor(tenant ?? OfferApiFactory.TenantA, RecuroRoles.HrTa);

    private HttpClient HrHead() => api.ClientFor(OfferApiFactory.TenantA, RecuroRoles.HrHead, "u-head", "K. Iyer");

    private HttpClient MdCeo() => api.ClientFor(OfferApiFactory.TenantA, RecuroRoles.MdCeo, "u-md", "R. Menon");

    private async Task PublishAsync(string type, string subject, object data, Guid? tenant = null)
    {
        var cloudEvent = new CloudEvent
        {
            Id = Guid.CreateVersion7(),
            Source = "/services/test",
            Type = type,
            Subject = subject,
            Time = DateTimeOffset.UtcNow,
            TenantId = tenant ?? OfferApiFactory.TenantA,
            ActorId = "u-ta",
            ActorName = "A. Sharma",
            ActorRole = RecuroRoles.HrTa,
            Data = JsonSerializer.SerializeToElement(data),
        };
        var processor = api.Services.GetRequiredService<IntegrationEventProcessor>();
        await processor.ProcessAsync(cloudEvent, CancellationToken.None);
    }

    private Task MoveAsync(string appId, string to, Guid? tenant = null) =>
        PublishAsync(
            "pipeline.stage.changed.v1",
            $"Application/{appId}",
            new { appId, reqId = "REQ-2026-0001", candidateId = "CAN-" + appId, source = "manual", from = "Interview", to, actor = "A. Sharma", at = DateTimeOffset.UtcNow, dwellMs = 1000 },
            tenant);

    private Task BgvAsync(string appId, string type, string? checkType = null, string? status = null) =>
        PublishAsync(type, $"Application/{appId}", new { caseId = Guid.NewGuid(), appId, reqId = "REQ-2026-0001", vendorId = "VEN-1", checkType, status });

    private static object Draft(string appId, decimal fix = 17, decimal variable = 2.8m) => new
    {
        appId,
        candidateName = "Sandeep Verma",
        joiningDate = "2026-11-02",
        probationMonths = 6,
        components = new { @fixed = fix, variable, benefits = 1.7m },
        band = new { min = 18, max = 24 },
    };

    private async Task<string> DraftAsync(string appId, Guid? tenant = null)
    {
        await MoveAsync(appId, "Selection", tenant);
        var response = await HrTa(tenant).PostAsJsonAsync(Offers, Draft(appId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;
    }

    private async Task<string> ApprovedAsync(string appId)
    {
        var id = await DraftAsync(appId);
        Assert.Equal(HttpStatusCode.OK, (await HrTa().PostAsync($"{Offers}/{id}/submit", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await HrHead().PostAsync($"{Offers}/{id}/approve", null)).StatusCode);
        return id;
    }

    private async Task<List<string>> OutboxTypesAsync(string subjectPart)
    {
        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfferDbContext>();
        var rows = await db.OutboxMessages.AsNoTracking().OrderBy(m => m.OccurredAt).ToListAsync();
        return rows.Where(r => r.Envelope.Contains(subjectPart, StringComparison.Ordinal)).Select(r => r.Type).ToList();
    }

    [Fact]
    public async Task Drafting_needs_a_selected_application_and_a_valid_ctc()
    {
        await MoveAsync("APP-EARLY", "Interview");
        Assert.Equal(HttpStatusCode.Conflict, (await HrTa().PostAsJsonAsync(Offers, Draft("APP-EARLY"))).StatusCode);

        await MoveAsync("APP-CTC", "Selection");
        var broken = await HrTa().PostAsJsonAsync(Offers, Draft("APP-CTC", fix: 4, variable: 5));
        Assert.Equal(HttpStatusCode.BadRequest, broken.StatusCode);
        Assert.Contains("ctc.fixed-min", await broken.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        var id = await DraftAsync("APP-DUP");
        Assert.Equal(HttpStatusCode.Conflict, (await HrTa().PostAsJsonAsync(Offers, Draft("APP-DUP"))).StatusCode);

        var offer = await HrTa().GetFromJsonAsync<JsonElement>($"{Offers}/{id}");
        Assert.Equal("M3", offer.GetProperty("grade").GetString());
        Assert.Equal("Draft", offer.GetProperty("state").GetString());
        Assert.Equal(21.5m, offer.GetProperty("components").GetProperty("fixed").GetDecimal() + 4.5m);
    }

    [Fact]
    public async Task A_deviation_routes_to_md_ceo_and_only_the_resolved_approver_decides()
    {
        await MoveAsync("APP-DEV", "Selection");
        var created = await HrTa().PostAsJsonAsync(Offers, Draft("APP-DEV", fix: 19.6m, variable: 3.2m));
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetString()!;

        var submitted = await (await HrTa().PostAsync($"{Offers}/{id}/submit", null)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PendingApproval", submitted.GetProperty("state").GetString());
        Assert.Equal("mdceo", submitted.GetProperty("route").GetProperty("approverRole").GetString());
        var start = api.Workflows.Started.Last();
        Assert.Equal("Deviation", start.Type);
        Assert.Equal(id, start.Subject.Id);

        var approved = await MdCeo().PostAsync($"{Offers}/{id}/approve", null);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);
        var body = await approved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Approved", body.GetProperty("state").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("components").ValueKind);
        Assert.Contains("offer.approved.v1", await OutboxTypesAsync($"Offer/{id}"));
    }

    [Fact]
    public async Task Release_is_blocked_until_bgv_clears_then_acceptance_publishes_the_joining_date()
    {
        var id = await ApprovedAsync("APP-REL");

        var blocked = await HrTa().PostAsync($"{Offers}/{id}/send", null);
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Contains("BGV", await blocked.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await BgvAsync("APP-REL", "bgv.case.initiated.v1");
        await BgvAsync("APP-REL", "bgv.cleared.v1");
        var sent = await HrTa().PostAsync($"{Offers}/{id}/send", null);
        Assert.Equal(HttpStatusCode.OK, sent.StatusCode);
        Assert.Equal("Sent", (await sent.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString());

        var letter = await HrTa().GetAsync($"{Offers}/{id}/letter");
        Assert.Equal("application/pdf", letter.Content.Headers.ContentType?.MediaType);

        var accepted = await HrTa().PostAsJsonAsync($"{Offers}/{id}/outcome", new { outcome = "Accepted" });
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(
            ["offer.submitted.v1", "offer.approved.v1", "offer.sent.v1", "offer.accepted.v1"],
            await OutboxTypesAsync($"Offer/{id}"));

        await using var scope = api.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfferDbContext>();
        var acceptedEvent = (await db.OutboxMessages.AsNoTracking().ToListAsync()).Single(m => m.Type == "offer.accepted.v1" && m.Envelope.Contains(id, StringComparison.Ordinal));
        using var envelope = JsonDocument.Parse(acceptedEvent.Envelope);
        Assert.Equal("2026-11-02", envelope.RootElement.GetProperty("data").GetProperty("joiningDate").GetString());
    }

    [Fact]
    public async Task Hr_head_may_send_a_conditional_offer_but_not_over_an_adverse_finding()
    {
        var id = await ApprovedAsync("APP-COND");
        await BgvAsync("APP-COND", "bgv.case.initiated.v1");

        Assert.Equal(HttpStatusCode.Forbidden, (await HrTa().PostAsJsonAsync($"{Offers}/{id}/send", new { conditional = true })).StatusCode);

        await BgvAsync("APP-COND", "bgv.adverse.flagged.v1", "Education");
        Assert.Equal(HttpStatusCode.Conflict, (await HrHead().PostAsJsonAsync($"{Offers}/{id}/send", new { conditional = true })).StatusCode);

        await BgvAsync("APP-COND", "bgv.resolved.v1", "Education", "ResolvedCleared");
        var conditional = await HrHead().PostAsJsonAsync($"{Offers}/{id}/send", new { conditional = true });
        Assert.Equal(HttpStatusCode.OK, conditional.StatusCode);
        Assert.True((await conditional.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("conditional").GetBoolean());
    }

    [Fact]
    public async Task An_override_that_clears_the_last_flag_opens_release_in_either_event_order()
    {
        foreach (var (appId, clearedFirst) in new[] { ("APP-OVR1", true), ("APP-OVR2", false) })
        {
            var id = await ApprovedAsync(appId);
            await BgvAsync(appId, "bgv.case.initiated.v1");
            await BgvAsync(appId, "bgv.adverse.flagged.v1", "Education", "Flagged");
            if (clearedFirst)
            {
                await BgvAsync(appId, "bgv.cleared.v1");
                await BgvAsync(appId, "bgv.resolved.v1", "Education", "ResolvedCleared");
            }
            else
            {
                await BgvAsync(appId, "bgv.resolved.v1", "Education", "ResolvedCleared");
                Assert.Equal(HttpStatusCode.Conflict, (await HrTa().PostAsync($"{Offers}/{id}/send", null)).StatusCode);
                await BgvAsync(appId, "bgv.cleared.v1");
            }

            Assert.Equal(HttpStatusCode.OK, (await HrTa().PostAsync($"{Offers}/{id}/send", null)).StatusCode);
        }

        var rescinded = await ApprovedAsync("APP-RES");
        await BgvAsync("APP-RES", "bgv.adverse.flagged.v1", "Employment", "Flagged");
        await BgvAsync("APP-RES", "bgv.resolved.v1", "Employment", "ResolvedAdverse");
        Assert.Equal(HttpStatusCode.Conflict, (await HrHead().PostAsJsonAsync($"{Offers}/{rescinded}/send", new { conditional = true })).StatusCode);
    }

    [Fact]
    public async Task The_e_sign_callback_must_be_signed_and_a_signed_letter_accepts_the_offer()
    {
        var id = await ApprovedAsync("APP-SIGN");
        await BgvAsync("APP-SIGN", "bgv.case.initiated.v1");
        await BgvAsync("APP-SIGN", "bgv.cleared.v1");
        await HrTa().PostAsync($"{Offers}/{id}/send", null);

        var body = JsonSerializer.Serialize(new
        {
            tenantId = OfferApiFactory.TenantA,
            offerId = id,
            letterVersion = 1,
            envelopeId = "env-1",
            status = "signed",
            signedDocument = Convert.ToBase64String("%PDF-1.4 signed"u8.ToArray()),
        });
        var anonymous = api.CreateClient();

        var unsigned = await anonymous.PostAsync($"{Offers}/esign/callback", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, unsigned.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Offers}/esign/callback") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        request.Headers.Add("X-Recuro-Signature", Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(OfferApiFactory.CallbackSecret), Encoding.UTF8.GetBytes(body))));
        Assert.True((await anonymous.SendAsync(request)).IsSuccessStatusCode);

        var offer = await HrTa().GetFromJsonAsync<JsonElement>($"{Offers}/{id}");
        Assert.Equal("Accepted", offer.GetProperty("state").GetString());
        Assert.Equal(HttpStatusCode.OK, (await HrTa().GetAsync($"{Offers}/{id}/letter?signed=true")).StatusCode);
    }

    [Fact]
    public async Task A_presigned_letter_link_opens_without_a_session_and_tampering_fails()
    {
        var id = await ApprovedAsync("APP-LINK");
        var link = await (await HrTa().PostAsync($"{Offers}/{id}/letter-link", null)).Content.ReadFromJsonAsync<JsonElement>();
        var path = new Uri(link.GetProperty("url").GetString()!).PathAndQuery;

        var anonymous = api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(path.Replace("/1?", "/2?", StringComparison.Ordinal))).StatusCode);
    }

    [Fact]
    public async Task Dashboard_counts_open_offers_and_another_tenant_sees_nothing()
    {
        var id = await DraftAsync("APP-TEN");
        await HrTa().PostAsync($"{Offers}/{id}/submit", null);

        var tile = await HrTa().GetFromJsonAsync<JsonElement>($"{Offers}/dashboard/ta");
        var stat = tile.GetProperty("stats")[0];
        Assert.Equal("Offers Pending", stat.GetProperty("label").GetString());
        Assert.Equal("t", stat.GetProperty("tone").GetString());
        Assert.True(int.Parse(stat.GetProperty("value").ToString(), CultureInfo.InvariantCulture) >= 1);

        Assert.Equal(HttpStatusCode.NotFound, (await HrTa(OfferApiFactory.TenantB).GetAsync($"{Offers}/{id}")).StatusCode);
        Assert.Equal(0, (await HrTa(OfferApiFactory.TenantB).GetFromJsonAsync<JsonElement>(Offers)).GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await api.ClientFor(OfferApiFactory.TenantA, RecuroRoles.Candidate).GetAsync(Offers)).StatusCode);
    }

    [Fact]
    public async Task Withdrawal_needs_hr_head_and_a_reason()
    {
        var id = await DraftAsync("APP-WD");
        Assert.Equal(HttpStatusCode.Forbidden, (await HrTa().PostAsJsonAsync($"{Offers}/{id}/withdraw", new { reason = "Position frozen by Finance." })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await HrHead().PostAsJsonAsync($"{Offers}/{id}/withdraw", new { reason = "no" })).StatusCode);
        var withdrawn = await HrHead().PostAsJsonAsync($"{Offers}/{id}/withdraw", new { reason = "Position frozen by Finance." });
        Assert.Equal("Withdrawn", (await withdrawn.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("state").GetString());
        Assert.Contains("offer.withdrawn.v1", await OutboxTypesAsync($"Offer/{id}"));
    }
}
