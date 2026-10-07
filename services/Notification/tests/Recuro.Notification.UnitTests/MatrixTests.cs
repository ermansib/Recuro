using Recuro.Notification.Application.Templates;
using Recuro.Notification.Domain.Matrix;

namespace Recuro.Notification.UnitTests;

public class MatrixTests
{
    [Fact]
    public void Every_rule_has_a_default_template()
    {
        var missing = NotificationMatrix.Rules.Select(r => r.TemplateKey).Except(DefaultTemplates.Keys).ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void Every_template_is_used_by_a_rule()
    {
        var unused = DefaultTemplates.Keys.Except(NotificationMatrix.Rules.Select(r => r.TemplateKey)).ToList();

        Assert.Empty(unused);
    }

    [Fact]
    public void Rules_only_name_known_roles_and_role_wide_rules_only_staff_roles()
    {
        foreach (var recipient in NotificationMatrix.Rules.SelectMany(r => r.Recipients))
        {
            Assert.Contains(recipient.Role, NotificationMatrix.KnownRoles);
            if (recipient.Kind == RecipientKind.Role || recipient.FallbackToRole)
            {
                Assert.Contains(recipient.Role, NotificationMatrix.BroadcastRoles);
            }
        }
    }

    [Fact]
    public void Payload_rules_name_their_field()
    {
        var unnamed = NotificationMatrix.Rules
            .SelectMany(r => r.Recipients)
            .Where(r => r.Kind is RecipientKind.PayloadUser or RecipientKind.PayloadUsers && string.IsNullOrWhiteSpace(r.Field));

        Assert.Empty(unnamed);
    }

    [Fact]
    public void Every_rule_has_a_channel_and_a_recipient()
    {
        Assert.All(NotificationMatrix.Rules, r =>
        {
            Assert.NotEqual(Channels.None, r.Channels);
            Assert.NotEmpty(r.Recipients);
        });
    }

    [Fact]
    public void Approvals_escalations_and_adverse_findings_are_critical()
    {
        string[] critical = ["workflow.task.created.v1", "workflow.escalated.v1", "bgv.adverse.flagged.v1", "pipeline.tat.breached.v1"];

        Assert.All(critical, type => Assert.All(NotificationMatrix.RulesFor(type), r => Assert.True(r.Critical)));
    }

    [Fact]
    public void Subscriptions_cover_rules_and_owner_events_once()
    {
        var types = NotificationMatrix.SubscribedEventTypes.ToList();

        Assert.Equal(types.Distinct().Count(), types.Count);
        Assert.Contains("recruitment.mrf.submitted.v1", types);
        Assert.Contains("bgv.adverse.flagged.v1", types);
    }
}
