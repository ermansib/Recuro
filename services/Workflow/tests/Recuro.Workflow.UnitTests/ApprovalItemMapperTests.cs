using Recuro.Workflow.Application.Approvals;
using Recuro.Workflow.Application.Workflows;
using Recuro.Workflow.Domain.Workflows;

namespace Recuro.Workflow.UnitTests;

public sealed class ApprovalItemMapperTests
{
    [Fact]
    public void Maps_to_the_frontend_approval_item()
    {
        var instance = TestData.Start();
        var task = instance.Tasks.Single();

        var item = ApprovalItemMapper.From(instance, task, ["hrhead"], TestData.Now.AddHours(1));

        Assert.Equal(task.Id.ToString(), item.Id);
        Assert.Equal("hrhead", item.AssigneeRole);
        Assert.Equal("MRF", item.Kind);
        Assert.Equal(new ChipDto("⏱ SLA 2d", "amber"), item.Chip);
        Assert.Equal(new SensitiveDto("Band", "₹18L – ₹24L"), item.Sensitive);
        Assert.Equal(new EntityRefDto("Requisition", "REQ-2026-0156"), item.Entity);
        Assert.Equal(["resolve", "reject", "query"], item.Actions.Select(a => a.Effect));
        Assert.Equal("2026-10-07T09:00:00.000Z", item.CreatedAt);
        Assert.True(item.IsNew);
        Assert.Null(item.Decision);
    }

    [Fact]
    public void Sensitive_values_are_masked_for_the_board()
    {
        var instance = TestData.Start(TestData.Leg("mdceo"));

        var item = ApprovalItemMapper.From(instance, instance.Tasks.Single(), ["mdceo"], TestData.Now);

        Assert.Equal(ApprovalItemMapper.Masked, item.Sensitive!.Value);
    }

    [Fact]
    public void Chips_follow_the_task_state()
    {
        var instance = TestData.Start(TestData.Leg("hrhead", 2, new LegEscalation("mdceo", "MD/CEO", 1)));
        var task = instance.Tasks.Single();

        Assert.Equal("red", ApprovalItemMapper.From(instance, task, ["hrhead"], TestData.Now.AddDays(2)).Chip.Tone);

        instance.FireDueEscalations(TestData.Now.AddDays(3));
        Assert.Equal("⚑ Escalated L1", ApprovalItemMapper.From(instance, task, ["hrhead"], TestData.Now.AddDays(3)).Chip.Text);

        instance.Decide(task.Id, TestData.HrHead, "approve", null, TaskSchedule.None, TestData.Now.AddDays(3));
        var decided = ApprovalItemMapper.From(instance, task, ["hrhead"], TestData.Now.AddDays(3));
        Assert.Equal("✓ Decided", decided.Chip.Text);
        Assert.Equal("K. Mehta", decided.Decision!.By);
        Assert.Null(decided.IsNew);
    }
}

public sealed class SlaPlannerTests
{
    [Fact]
    public async Task Deadlines_use_the_pinned_calendar_version_and_keep_the_time_of_day()
    {
        var calendar = new FakeCalendar();
        var planner = new SlaPlanner(calendar);
        var leg = TestData.Leg("hrhead", 2, new LegEscalation("mdceo", "MD/CEO", 1), new LegEscalation("board", "Board", 3));

        var schedule = await planner.ScheduleAsync(leg, "doa-2026.08", TestData.Now, CancellationToken.None);

        Assert.Equal(TestData.Now.AddDays(2), schedule.DueAt);
        Assert.Equal([TestData.Now.AddDays(3), TestData.Now.AddDays(5)], schedule.EscalationsAt);
        Assert.All(calendar.Calls, c => Assert.Equal("doa-2026.08", c.Version));
    }

    [Fact]
    public async Task A_leg_without_an_sla_has_no_deadline()
    {
        var calendar = new FakeCalendar();

        var schedule = await new SlaPlanner(calendar).ScheduleAsync(TestData.Leg("hrhead", null), "v", TestData.Now, CancellationToken.None);

        Assert.Same(TaskSchedule.None, schedule);
        Assert.Empty(calendar.Calls);
    }
}
