using Recuro.BuildingBlocks.Domain;
using Recuro.Workflow.Domain.Workflows;

namespace Recuro.Workflow.UnitTests;

public sealed class WorkflowInstanceTests
{
    private static readonly DateTimeOffset Later = TestData.Now.AddHours(3);

    [Fact]
    public void Starting_opens_one_task_per_assignee_of_the_first_leg()
    {
        var leg = new LegDefinition("Approving", [new LegAssignee("hrhead", "HR Head"), new LegAssignee("mdceo", "MD/CEO")], 2, []);
        var instance = TestData.Start(leg);

        Assert.Equal(WorkflowStatus.Active, instance.Status);
        Assert.Equal(["hrhead", "mdceo"], instance.Tasks.Select(t => t.AssigneeRole));
        Assert.All(instance.Tasks, t => Assert.Equal(TestData.Now.AddDays(2), t.DueAt));
        Assert.Equal(2, instance.DomainEvents.OfType<TaskCreated>().Count());
    }

    [Fact]
    public void A_route_without_assignees_is_rejected()
    {
        var result = WorkflowInstance.Start(
            "MRF", "Requisition", "REQ-1", "k", "v", [], TestData.Presentation(), TestData.Initiator, TaskSchedule.None, TestData.Now);

        Assert.True(result.IsFailure);
        Assert.Equal(ErrorType.Validation, result.Error!.Type);
    }

    [Fact]
    public void Approving_the_last_leg_approves_the_workflow()
    {
        var instance = TestData.Start();
        var task = instance.Tasks.Single();

        var result = instance.Decide(task.Id, TestData.HrHead, "approve", null, TaskSchedule.None, Later);

        Assert.True(result.IsSuccess);
        Assert.Equal(WorkflowStatus.Approved, instance.Status);
        Assert.Equal("Approved", task.Decision!.Text);
        Assert.Equal("K. Mehta", task.Decision.ByName);
        var completed = Assert.Single(instance.DomainEvents.OfType<TaskCompleted>());
        Assert.Same(task, completed.Task);
    }

    [Fact]
    public void Legs_run_in_order()
    {
        var instance = TestData.Start(TestData.Leg("hrhead"), TestData.Leg("mdceo", 3));
        var first = instance.Tasks.Single();

        instance.Decide(first.Id, TestData.HrHead, "approve", null, TestData.Schedule(3), Later);

        Assert.Equal(WorkflowStatus.Active, instance.Status);
        Assert.Equal(1, instance.CurrentLeg);
        var second = instance.Tasks.Single(t => t.LegIndex == 1);
        Assert.Equal("mdceo", second.AssigneeRole);
        Assert.Equal(TestData.Now.AddDays(3), second.DueAt);

        instance.Decide(second.Id, TestData.MdCeo, "approve", null, TaskSchedule.None, Later);
        Assert.Equal(WorkflowStatus.Approved, instance.Status);
    }

    [Fact]
    public void Only_the_assignee_decides()
    {
        var instance = TestData.Start();

        var result = instance.Decide(instance.Tasks.Single().Id, TestData.MdCeo, "approve", null, TaskSchedule.None, Later);

        Assert.Equal(ErrorType.Forbidden, result.Error!.Type);
        Assert.Equal(WorkflowStatus.Active, instance.Status);
    }

    [Fact]
    public void Decisions_are_final()
    {
        var instance = TestData.Start(TestData.Leg("hrhead"), TestData.Leg("mdceo"));
        var task = instance.Tasks.Single();
        instance.Decide(task.Id, TestData.HrHead, "approve", null, TaskSchedule.None, Later);

        var again = instance.Decide(task.Id, TestData.HrHead, "reject", "Changed my mind entirely.", TaskSchedule.None, Later);

        Assert.Equal(ErrorType.Conflict, again.Error!.Type);
        Assert.Equal("already_decided", again.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("too short")]
    public void Rejecting_needs_a_documented_reason(string? reason)
    {
        var instance = TestData.Start();

        var result = instance.Decide(instance.Tasks.Single().Id, TestData.HrHead, "reject", reason, TaskSchedule.None, Later);

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal(WorkflowStatus.Active, instance.Status);
    }

    [Fact]
    public void A_rejection_ends_the_workflow_and_withdraws_parallel_tasks()
    {
        var leg = new LegDefinition("Approving", [new LegAssignee("hrhead", "HR Head"), new LegAssignee("mdceo", "MD/CEO")], 2, []);
        var instance = TestData.Start(leg);
        var hrhead = instance.Tasks.Single(t => t.AssigneeRole == "hrhead");

        instance.Decide(hrhead.Id, TestData.HrHead, "reject", "Budget not available this quarter.", TaskSchedule.None, Later);

        Assert.Equal(WorkflowStatus.Rejected, instance.Status);
        Assert.Equal("Budget not available this quarter.", hrhead.Decision!.Reason);
        Assert.Equal(ApprovalTaskStatus.Cancelled, instance.Tasks.Single(t => t.AssigneeRole == "mdceo").Status);
    }

    [Fact]
    public void Parallel_assignees_must_all_approve()
    {
        var leg = new LegDefinition("Approving", [new LegAssignee("hrhead", "HR Head"), new LegAssignee("mdceo", "MD/CEO")], 2, []);
        var instance = TestData.Start(leg);

        instance.Decide(instance.Tasks.Single(t => t.AssigneeRole == "hrhead").Id, TestData.HrHead, "approve", null, TaskSchedule.None, Later);
        Assert.Equal(WorkflowStatus.Active, instance.Status);

        instance.Decide(instance.Tasks.Single(t => t.AssigneeRole == "mdceo").Id, TestData.MdCeo, "approve", null, TaskSchedule.None, Later);
        Assert.Equal(WorkflowStatus.Approved, instance.Status);
    }

    [Fact]
    public void A_query_pauses_the_sla_and_resuming_moves_the_deadline()
    {
        var instance = TestData.Start(TestData.Leg("hrhead", 2, new LegEscalation("mdceo", "MD/CEO", 1)));
        var task = instance.Tasks.Single();

        instance.Decide(task.Id, TestData.HrHead, "query", "Why two positions?", TaskSchedule.None, Later);

        Assert.True(task.IsOpen);
        Assert.Equal(Later, task.PausedAt);
        Assert.Single(instance.DomainEvents.OfType<SlaPaused>());
        Assert.Equal(0, instance.FireDueEscalations(TestData.Now.AddDays(10)));

        var resumedAt = Later.AddDays(1);
        Assert.True(instance.Resume(task.Id, resumedAt).IsSuccess);

        Assert.Null(task.PausedAt);
        Assert.Equal(TestData.Now.AddDays(3), task.DueAt);
        Assert.Equal(TestData.Now.AddDays(4), task.NextEscalationAt);
        Assert.Single(instance.DomainEvents.OfType<SlaResumed>());
    }

    [Fact]
    public void Resuming_without_a_query_is_a_conflict()
    {
        var instance = TestData.Start();

        Assert.Equal("not_paused", instance.Resume(instance.Tasks.Single().Id, Later).Error!.Code);
    }

    [Fact]
    public void Escalation_steps_fire_once_each_when_due()
    {
        var instance = TestData.Start(TestData.Leg("hrhead", 2, new LegEscalation("mdceo", "MD/CEO", 1)));
        var task = instance.Tasks.Single();

        Assert.Equal(1, instance.FireDueEscalations(TestData.Now.AddDays(2))); // the 100% reminder
        Assert.Equal(1, instance.FireDueEscalations(TestData.Now.AddDays(3)));
        Assert.Equal(0, instance.FireDueEscalations(TestData.Now.AddDays(4)));

        Assert.Equal(1, task.EscalationLevel);
        Assert.Null(task.NextEscalationAt);
        var escalated = Assert.Single(instance.DomainEvents.OfType<TaskEscalated>());
        Assert.Equal("mdceo", escalated.Step.Role);
    }

    [Fact]
    public void Cancelling_withdraws_open_tasks_and_is_idempotent()
    {
        var instance = TestData.Start();

        Assert.True(instance.Cancel("Requisition submit failed.", Later).IsSuccess);
        Assert.True(instance.Cancel("again", Later).IsSuccess);

        Assert.Equal(WorkflowStatus.Cancelled, instance.Status);
        Assert.Equal(ApprovalTaskStatus.Cancelled, instance.Tasks.Single().Status);
        Assert.Equal("Requisition submit failed.", instance.CancelReason);
    }

    [Fact]
    public void A_decided_workflow_cannot_be_cancelled()
    {
        var instance = TestData.Start();
        instance.Decide(instance.Tasks.Single().Id, TestData.HrHead, "approve", null, TaskSchedule.None, Later);

        Assert.Equal(ErrorType.Conflict, instance.Cancel("too late", Later).Error!.Type);
    }

    [Fact]
    public void Reminders_fire_at_half_and_full_sla_once_each()
    {
        var schedule = new TaskSchedule(TestData.Now.AddDays(2), [], TestData.Now.AddDays(1));
        var result = WorkflowInstance.Start(
            "MRF", "Requisition", "REQ-1", "k", "v", [TestData.Leg("hrhead")], TestData.Presentation(), TestData.Initiator, schedule, TestData.Now);
        var instance = result.Value;
        var task = instance.Tasks.Single();

        Assert.Equal(0, instance.FireDueEscalations(TestData.Now.AddHours(23)));
        Assert.Equal(1, instance.FireDueEscalations(TestData.Now.AddDays(1)));
        Assert.Equal(TestData.Now.AddDays(2), task.NextReminderAt);
        Assert.Equal(1, instance.FireDueEscalations(TestData.Now.AddDays(2)));
        Assert.Equal(0, instance.FireDueEscalations(TestData.Now.AddDays(5)));

        Assert.Equal([50, 100], instance.DomainEvents.OfType<TaskReminderDue>().Select(e => e.Step.ThresholdPercent));
        Assert.Null(task.NextReminderAt);
    }

    [Fact]
    public void Deciding_cancels_pending_reminders()
    {
        var instance = TestData.Start();
        var task = instance.Tasks.Single();

        instance.Decide(task.Id, TestData.HrHead, "approve", null, TaskSchedule.None, TestData.Now.AddHours(1));

        Assert.Null(task.NextReminderAt);
        Assert.Equal(0, instance.FireDueEscalations(TestData.Now.AddDays(5)));
    }
}
