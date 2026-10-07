using Recuro.BuildingBlocks.Domain;
using Recuro.Requisition.Domain.Requisitions;

namespace Recuro.Requisition.UnitTests;

public class RequisitionTests
{
    private static ManpowerRequisition Draft(RequisitionDetails? details = null) =>
        ManpowerRequisition.CreateDraft(details ?? TestData.CompleteDetails(), "u-1", "A. Sharma", TestData.Now);

    private static ManpowerRequisition Submitted()
    {
        var requisition = Draft();
        requisition.IssueReqId("REQ-2026-0001");
        var plan = new SubmissionPlan(new ApprovalRoute("HOD", "Function Head", "HR Head", "hrhead"), "doa-1", new DateOnly(2026, 11, 20), Guid.NewGuid());
        Assert.True(requisition.MarkSubmitted(plan, TestData.Now).IsSuccess);
        requisition.ClearDomainEvents();
        return requisition;
    }

    [Fact]
    public void Every_state_has_a_row_and_matches_the_frontend_table()
    {
        var table = RequisitionTransitions.Table;

        Assert.True(table.CanMove(RequisitionState.Draft, RequisitionState.PendingApproval));
        Assert.True(table.CanMove(RequisitionState.PendingApproval, RequisitionState.Draft));
        Assert.True(table.CanMove(RequisitionState.Rejected, RequisitionState.Draft));
        Assert.Empty(table.AllowedFrom(RequisitionState.Filled));
        Assert.Empty(table.AllowedFrom(RequisitionState.Cancelled));
        Assert.False(table.CanMove(RequisitionState.Draft, RequisitionState.Approved));
    }

    [Fact]
    public void A_new_draft_has_a_draft_id_and_no_req_id_yet()
    {
        var draft = Draft();

        Assert.StartsWith("DRAFT-", draft.ReqId, StringComparison.Ordinal);
        Assert.False(draft.HasIssuedReqId);
        Assert.Equal(RequisitionState.Draft, draft.State);
    }

    [Fact]
    public void Submit_lists_every_missing_annexure_A_field()
    {
        var empty = new RequisitionDetails("", "", "", "", 0, "", EmploymentType.Permanent, RequisitionNature.Replacement, "", null, "", true, "short", "", []);

        var result = Draft(empty).EnsureCanSubmit();

        Assert.Equal(ErrorType.Validation, result.Error!.Type);
        Assert.Equal(
            ["band", "department", "designation", "grade", "joiningDate", "location", "oobJustification", "positions", "qualifications", "replacementReason", "reportingManager"],
            result.Error.Fields.Select(f => f.Field).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Submitting_moves_to_pending_approval_and_raises_the_submitted_event()
    {
        var draft = Draft(TestData.CompleteDetails(outOfBudget: true));
        draft.IssueReqId("REQ-2026-0007");
        var instance = Guid.NewGuid();

        var result = draft.MarkSubmitted(new SubmissionPlan(new ApprovalRoute("HOD", "FH", "HR Head + MD/CEO (OOB)", "mdceo"), "doa-1", new DateOnly(2026, 11, 20), instance), TestData.Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(RequisitionState.PendingApproval, draft.State);
        Assert.Equal("doa-1", draft.ConfigVersionId);
        var submitted = Assert.IsType<RequisitionSubmitted>(Assert.Single(draft.DomainEvents));
        Assert.True(submitted.OutOfBudget);
        Assert.Equal(instance, submitted.WorkflowInstanceId);
    }

    [Fact]
    public void Sourcing_stays_locked_until_the_MRF_is_approved()
    {
        var requisition = Submitted();
        Assert.Equal("sourcing_locked", requisition.EnsureSourcingAllowed().Error!.Code);

        requisition.ApplyApprovalOutcome(approved: true, reason: null, TestData.Now);

        Assert.True(requisition.EnsureSourcingAllowed().IsSuccess);
        Assert.IsType<RequisitionApproved>(Assert.Single(requisition.DomainEvents));
    }

    [Fact]
    public void A_redelivered_approval_is_a_no_op()
    {
        var requisition = Submitted();
        requisition.ApplyApprovalOutcome(true, null, TestData.Now);
        requisition.ClearDomainEvents();

        Assert.True(requisition.ApplyApprovalOutcome(true, null, TestData.Now).IsSuccess);
        Assert.Empty(requisition.DomainEvents);
    }

    [Fact]
    public void Rejection_stores_the_reason()
    {
        var requisition = Submitted();

        requisition.ApplyApprovalOutcome(approved: false, reason: "Budget frozen this quarter", TestData.Now);

        Assert.Equal(RequisitionState.Rejected, requisition.State);
        Assert.Equal("Budget frozen this quarter", requisition.Reason);
        Assert.False(requisition.SourcingAllowed);
    }

    [Fact]
    public void A_draft_cannot_be_approved()
    {
        Assert.Equal("illegal_transition", Draft().ApplyApprovalOutcome(true, null, TestData.Now).Error!.Code);
    }

    [Fact]
    public void Cancelling_needs_a_documented_reason()
    {
        var requisition = Submitted();
        requisition.ApplyApprovalOutcome(true, null, TestData.Now);
        requisition.ClearDomainEvents();

        Assert.Equal(ErrorType.Validation, requisition.Cancel("no", TestData.Now).Error!.Type);
        Assert.True(requisition.Cancel("Position merged into REQ-2026-0002", TestData.Now).IsSuccess);
        Assert.Equal(RequisitionState.Cancelled, requisition.State);
        Assert.Equal(RequisitionState.Approved, Assert.IsType<RequisitionCancelled>(Assert.Single(requisition.DomainEvents)).From);
    }

    [Fact]
    public void A_pending_approval_cannot_be_cancelled_per_the_FRD_table()
    {
        Assert.Equal("illegal_transition", Submitted().Cancel("Position merged into REQ-2026-0002", TestData.Now).Error!.Code);
    }

    [Fact]
    public void Only_drafts_can_be_edited()
    {
        Assert.Equal("requisition_not_draft", Submitted().UpdateDraft(TestData.CompleteDetails()).Error!.Code);
    }

    [Fact]
    public void A_req_id_is_issued_only_once()
    {
        var draft = Draft();
        draft.IssueReqId("REQ-2026-0001");

        Assert.Throws<InvalidOperationException>(() => draft.IssueReqId("REQ-2026-0002"));
    }
}
