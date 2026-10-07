using Microsoft.Extensions.Logging.Abstractions;
using Recuro.Requisition.Application.Abstractions;
using Recuro.Requisition.Application.Requisitions;
using Recuro.Requisition.Domain.Requisitions;

namespace Recuro.Requisition.UnitTests;

public class SubmissionSagaTests
{
    private readonly FakeRules _rules = new();
    private readonly FakeWorkflows _workflows = new();
    private readonly FakeUnitOfWork _unitOfWork = new();

    private RequisitionSubmission Saga() =>
        new(_rules, _workflows, new FakeReqIds(), _unitOfWork, new FixedClock(TestData.Now), NullLogger<RequisitionSubmission>.Instance);

    private static ManpowerRequisition Draft(RequisitionDetails? details = null) =>
        ManpowerRequisition.CreateDraft(details ?? TestData.CompleteDetails(), "u-1", "A. Sharma", TestData.Now);

    [Fact]
    public async Task Submit_resolves_the_route_opens_a_workflow_and_pins_the_config_version()
    {
        var draft = Draft();

        var result = await Saga().SubmitAsync(draft, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("REQ-2026-0001", draft.ReqId);
        Assert.Equal(RequisitionState.PendingApproval, draft.State);
        Assert.Equal("doa-2026.08", draft.ConfigVersionId);
        Assert.Equal(new DateOnly(2026, 11, 11), draft.TargetClosure);
        Assert.Equal("hrhead", draft.Route.ApproverRole);

        var start = Assert.Single(_workflows.Started);
        Assert.Equal("MRF", start.Type);
        Assert.Equal(new WorkflowSubject("Requisition", "REQ-2026-0001"), start.Subject);
        Assert.Equal("MRF Approval — REQ-2026-0001", start.Presentation.Title);
        Assert.Equal(["approve", "reject", "query"], start.Presentation.Actions.Select(a => a.Id));
        Assert.Equal(2, start.Legs[0].SlaWorkingDays);
    }

    [Fact]
    public async Task An_unknown_grade_is_a_validation_error_and_nothing_starts()
    {
        _rules.Resolution = null;
        var draft = Draft();

        var result = await Saga().SubmitAsync(draft, CancellationToken.None);

        Assert.Equal("unknown_grade", result.Error!.Fields.Single().Code);
        Assert.Equal(RequisitionState.Draft, draft.State);
        Assert.Empty(_workflows.Started);
    }

    [Fact]
    public async Task If_the_workflow_cannot_be_opened_the_requisition_stays_a_draft_but_keeps_its_req_id()
    {
        _workflows.FailOnStart = true;
        var draft = Draft();

        await Assert.ThrowsAsync<DependencyUnavailableException>(() => Saga().SubmitAsync(draft, CancellationToken.None));

        Assert.Equal(RequisitionState.Draft, draft.State);
        Assert.Equal("REQ-2026-0001", draft.ReqId);
    }

    [Fact]
    public async Task If_the_commit_fails_after_the_workflow_opened_the_workflow_is_cancelled()
    {
        // Save 1 persists the REQ-ID on the draft; save 2 is the submit commit.
        _unitOfWork.FailOnSave = 2;
        var draft = Draft();

        await Assert.ThrowsAsync<InvalidOperationException>(() => Saga().SubmitAsync(draft, CancellationToken.None));

        Assert.Single(_workflows.Cancelled);
    }

    [Fact]
    public async Task A_route_without_legs_falls_back_to_the_approving_authority_without_an_sla()
    {
        _rules.Resolution = TestData.Doa("mdceo") with { Legs = [] };

        await Saga().SubmitAsync(Draft(), CancellationToken.None);

        var leg = Assert.Single(Assert.Single(_workflows.Started).Legs);
        Assert.Equal("mdceo", Assert.Single(leg.Assignees).Role);
        Assert.Null(leg.SlaWorkingDays);
    }
}
