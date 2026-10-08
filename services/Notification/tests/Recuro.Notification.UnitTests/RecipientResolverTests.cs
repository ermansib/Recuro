using System.Text.Json;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Application.Events;
using Recuro.Notification.Domain.Matrix;

namespace Recuro.Notification.UnitTests;

public class RecipientResolverTests
{
    private static readonly EventMetadata Metadata = new(
        Guid.NewGuid(), "interview.scheduled.v1", "/services/interview", "Interview/R-1", DateTimeOffset.UtcNow,
        Guid.NewGuid(), null, "cand-7", "P. Nair", "candidate");

    [Fact]
    public void Payload_values_flatten_scalars_and_lists_and_add_the_subject_id()
    {
        var data = JsonSerializer.SerializeToElement(new { appId = "APP-1", panel = new[] { "u-1", "u-2" }, avg = 3.5, nested = new { x = 1 } });

        var values = EventPayload.TemplateValues(Metadata, data);

        Assert.Equal("APP-1", values["appId"]);
        Assert.Equal("u-1, u-2", values["panel"]);
        Assert.Equal("3.5", values["avg"]);
        Assert.Equal("R-1", values["subjectId"]);
        Assert.Equal("P. Nair", values["actorName"]);
        Assert.False(values.ContainsKey("nested"));
    }

    [Theory]
    [InlineData(187_200_000L, "2d 4h")]
    [InlineData(12_000_000L, "3h 20m")]
    [InlineData(2_700_000L, "45m")]
    public void Millisecond_durations_also_read_as_text(long varianceMs, string expected)
    {
        var values = EventPayload.TemplateValues(Metadata, JsonSerializer.SerializeToElement(new { varianceMs }));

        Assert.Equal(expected, values["variance"]);
        Assert.Equal(varianceMs.ToString(System.Globalization.CultureInfo.InvariantCulture), values["varianceMs"]);
    }

    [Fact]
    public void Lists_of_objects_read_as_their_labels_and_gateway_paths_as_links()
    {
        var data = JsonSerializer.SerializeToElement(new
        {
            documents = new[] { new { type = "id", label = "Identity proof" }, new { type = "pan", label = "PAN card" } },
            pdfPath = "/api/v1/reports/packs/p-1/pdf",
            otherPath = "relative/x",
        });

        var values = EventPayload.TemplateValues(Metadata, data, new Uri("https://portal.example.test/"));

        Assert.Equal("Identity proof, PAN card", values["documents"]);
        Assert.Equal("https://portal.example.test/api/v1/reports/packs/p-1/pdf", values["pdfUrl"]);
        Assert.False(values.ContainsKey("otherUrl"));
        Assert.False(EventPayload.TemplateValues(Metadata, data).ContainsKey("pdfUrl"));
    }

    [Fact]
    public async Task A_role_with_nobody_in_the_directory_gets_one_role_wide_item_and_one_unaddressed_email()
    {
        var resolver = new RecipientResolver(new FakeStore(), FakeContacts.None, FakeStaff.None, TimeProvider.System);

        var result = await resolver.ResolveAsync(RecipientRule.ForRole("hrhead"), Metadata, default, CancellationToken.None);

        Assert.True(Assert.Single(result.InApp).IsRoleWide);
        Assert.Null(Assert.Single(result.Email).Email);
    }

    [Fact]
    public async Task A_role_known_to_the_directory_gets_an_email_per_person()
    {
        var store = new FakeStore();
        store.AddUser("u-1", "R. Iyer", "r.iyer@example.test", "hrhead");
        store.AddUser("u-2", "S. Rao", "s.rao@example.test", "hrhead");
        store.AddUser("u-3", "T. Das", "t.das@example.test", "hrta");

        var result = await new RecipientResolver(store, FakeContacts.None, FakeStaff.None, TimeProvider.System).ResolveAsync(RecipientRule.ForRole("hrhead"), Metadata, default, CancellationToken.None);

        Assert.True(Assert.Single(result.InApp).IsRoleWide);
        Assert.Equal(["r.iyer@example.test", "s.rao@example.test"], result.Email.Select(r => r.Email));
    }

    [Fact]
    public async Task Identitys_list_wins_over_the_local_directory_and_is_remembered()
    {
        var store = new FakeStore();
        store.AddUser("u-1", "Old Name", "old@example.test", "hrhead");
        var staff = new FakeStaff([new StaffContact("u-1", "R. Iyer", "r.iyer@example.test"), new StaffContact("u-9", "N. Shah", "n.shah@example.test")]);

        var result = await new RecipientResolver(store, FakeContacts.None, staff, TimeProvider.System).ResolveAsync(RecipientRule.ForRole("hrhead"), Metadata, default, CancellationToken.None);

        Assert.Equal(["r.iyer@example.test", "n.shah@example.test"], result.Email.Select(r => r.Email));
        Assert.Equal("n.shah@example.test", (await store.FindUserAsync("u-9", CancellationToken.None))?.Email);
        Assert.Contains("hrhead", (await store.FindUserAsync("u-9", CancellationToken.None))!.Roles);
    }

    [Fact]
    public async Task The_actor_is_addressed_by_id_and_keeps_their_name()
    {
        var result = await new RecipientResolver(new FakeStore(), FakeContacts.None, FakeStaff.None, TimeProvider.System).ResolveAsync(RecipientRule.ForActor("candidate"), Metadata, default, CancellationToken.None);

        var recipient = Assert.Single(result.InApp);
        Assert.Equal("cand-7", recipient.UserId);
        Assert.Equal("P. Nair", recipient.Name);
        Assert.Equal("candidate", recipient.Role);
    }

    [Fact]
    public async Task A_missing_payload_user_falls_back_to_the_role_when_the_rule_says_so()
    {
        var data = JsonSerializer.SerializeToElement(new { taskId = "T-1" });
        var resolver = new RecipientResolver(new FakeStore(), FakeContacts.None, FakeStaff.None, TimeProvider.System);

        var withFallback = await resolver.ResolveAsync(RecipientRule.ForPayloadUser("assignee", "hrhead", fallbackToRole: true), Metadata, data, CancellationToken.None);
        var without = await resolver.ResolveAsync(RecipientRule.ForPayloadUser("assignee", "hrhead"), Metadata, data, CancellationToken.None);

        Assert.True(Assert.Single(withFallback.InApp).IsRoleWide);
        Assert.Empty(without.InApp);
    }

    [Fact]
    public async Task A_payload_value_naming_a_staff_role_reaches_that_role()
    {
        // Workflow assigns tasks to roles: { "assignee": "mdceo" } and { "assigneeIds": ["hrhead", "u-7"] }.
        var resolver = new RecipientResolver(new FakeStore(), FakeContacts.None, FakeStaff.None, TimeProvider.System);

        var single = await resolver.ResolveAsync(RecipientRule.ForPayloadUser("assignee", "hrhead", fallbackToRole: true), Metadata,
            JsonSerializer.SerializeToElement(new { assignee = "mdceo" }), CancellationToken.None);
        var mixed = await resolver.ResolveAsync(RecipientRule.ForPayloadUsers("assigneeIds", "hrhead"), Metadata,
            JsonSerializer.SerializeToElement(new { assigneeIds = new[] { "hrhead", "u-7" } }), CancellationToken.None);

        var role = Assert.Single(single.InApp);
        Assert.True(role.IsRoleWide);
        Assert.Equal("mdceo", role.Role);
        Assert.Equal([(true, "hrhead", null), (false, "hrhead", "u-7")], mixed.InApp.Select(r => (r.IsRoleWide, r.Role, r.UserId)));
    }

    [Fact]
    public async Task Panel_members_are_deduplicated()
    {
        var data = JsonSerializer.SerializeToElement(new { panel = new[] { "u-1", "u-1", "u-2" } });

        var result = await new RecipientResolver(new FakeStore(), FakeContacts.None, FakeStaff.None, TimeProvider.System).ResolveAsync(RecipientRule.ForPayloadUsers("panel", "hrta"), Metadata, data, CancellationToken.None);

        Assert.Equal(["u-1", "u-2"], result.InApp.Select(r => r.UserId));
    }

    [Fact]
    public async Task A_candidate_gets_email_only_at_Candidates_address_and_never_a_bell_item()
    {
        var data = JsonSerializer.SerializeToElement(new { candidateId = "CND-1" });
        var contacts = new FakeContacts(new Recuro.Notification.Application.Abstractions.CandidateContact("P. Nair", "p.nair@example.test"));

        var result = await new RecipientResolver(new FakeStore(), contacts, FakeStaff.None, TimeProvider.System).ResolveAsync(RecipientRule.ForPayloadCandidate("candidateId"), Metadata, data, CancellationToken.None);

        Assert.Empty(result.InApp);
        var recipient = Assert.Single(result.Email);
        Assert.Equal("candidate:CND-1", recipient.UserId);
        Assert.Equal("p.nair@example.test", recipient.Email);
    }

    [Fact]
    public void Dates_in_payloads_are_read_as_midnight_UTC()
    {
        var data = JsonSerializer.SerializeToElement(new { regretSendAt = "2026-10-12", bad = "soon" });

        Assert.Equal(new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.Zero), EventPayload.ReadDate(data, "regretSendAt"));
        Assert.Null(EventPayload.ReadDate(data, "bad"));
        Assert.Null(EventPayload.ReadDate(data, "missing"));
    }
}
