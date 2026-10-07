using System.Text.Json;
using Recuro.BuildingBlocks.Application.IntegrationEvents;
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

    [Fact]
    public async Task A_role_with_nobody_in_the_directory_gets_one_role_wide_item_and_one_unaddressed_email()
    {
        var resolver = new RecipientResolver(new FakeStore());

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

        var result = await new RecipientResolver(store).ResolveAsync(RecipientRule.ForRole("hrhead"), Metadata, default, CancellationToken.None);

        Assert.True(Assert.Single(result.InApp).IsRoleWide);
        Assert.Equal(["r.iyer@example.test", "s.rao@example.test"], result.Email.Select(r => r.Email));
    }

    [Fact]
    public async Task The_actor_is_addressed_by_id_and_keeps_their_name()
    {
        var result = await new RecipientResolver(new FakeStore()).ResolveAsync(RecipientRule.ForActor("candidate"), Metadata, default, CancellationToken.None);

        var recipient = Assert.Single(result.InApp);
        Assert.Equal("cand-7", recipient.UserId);
        Assert.Equal("P. Nair", recipient.Name);
        Assert.Equal("candidate", recipient.Role);
    }

    [Fact]
    public async Task A_missing_payload_user_falls_back_to_the_role_when_the_rule_says_so()
    {
        var data = JsonSerializer.SerializeToElement(new { taskId = "T-1" });
        var resolver = new RecipientResolver(new FakeStore());

        var withFallback = await resolver.ResolveAsync(RecipientRule.ForPayloadUser("assignee", "hrhead", fallbackToRole: true), Metadata, data, CancellationToken.None);
        var without = await resolver.ResolveAsync(RecipientRule.ForPayloadUser("assignee", "hrhead"), Metadata, data, CancellationToken.None);

        Assert.True(Assert.Single(withFallback.InApp).IsRoleWide);
        Assert.Empty(without.InApp);
    }

    [Fact]
    public async Task Panel_members_are_deduplicated()
    {
        var data = JsonSerializer.SerializeToElement(new { panel = new[] { "u-1", "u-1", "u-2" } });

        var result = await new RecipientResolver(new FakeStore()).ResolveAsync(RecipientRule.ForPayloadUsers("panel", "hrta"), Metadata, data, CancellationToken.None);

        Assert.Equal(["u-1", "u-2"], result.InApp.Select(r => r.UserId));
    }
}
