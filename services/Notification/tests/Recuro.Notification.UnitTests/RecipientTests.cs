using Recuro.Notification.Domain.Feed;

namespace Recuro.Notification.UnitTests;

public class RecipientTests
{
    private static readonly RenderedNotification Content = new("📋", "Title", "Body", "/approvals");
    private static readonly DeliveryOrigin Origin = new(Guid.NewGuid(), "x.v1", "t", "v", "m", Critical: false);

    [Theory]
    [InlineData("hrta")]
    [InlineData("hrhead")]
    [InlineData("mdceo")]
    public void Staff_roles_may_receive_role_wide_items(string role)
    {
        Assert.True(FeedItem.Create(Recipient.Everyone(role), Content, Origin, DateTimeOffset.UtcNow).IsSuccess);
    }

    [Theory]
    [InlineData("candidate")]
    [InlineData("employee")]
    public void Candidates_and_employees_never_get_role_wide_broadcasts(string role)
    {
        var result = FeedItem.Create(Recipient.Everyone(role), Content, Origin, DateTimeOffset.UtcNow);

        Assert.Equal("role_wide_not_allowed", result.Error!.Code);
    }

    [Fact]
    public void Unknown_roles_are_refused()
    {
        Assert.Equal("unknown_role", FeedItem.Create(new Recipient("admin", "u-1", null, null), Content, Origin, DateTimeOffset.UtcNow).Error!.Code);
    }

    [Fact]
    public void A_user_item_is_visible_only_to_that_user_and_a_role_item_to_the_role()
    {
        var mine = FeedItem.Create(new Recipient("candidate", "c-1", null, null), Content, Origin, DateTimeOffset.UtcNow).Value;
        var team = FeedItem.Create(Recipient.Everyone("hrta"), Content, Origin, DateTimeOffset.UtcNow).Value;

        Assert.True(mine.IsVisibleTo("c-1", ["candidate"]));
        Assert.False(mine.IsVisibleTo("c-2", ["candidate"]));
        Assert.True(team.IsVisibleTo("u-9", ["hrta"]));
        Assert.False(team.IsVisibleTo("u-9", ["hrhead"]));
    }
}
