using Recuro.Onboarding.Domain.Bgv;
using Recuro.Onboarding.Domain.Cases;
using Recuro.Onboarding.Domain.Rules;

namespace Recuro.Onboarding.UnitTests;

public sealed class OnboardingCaseTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 12, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Joining = new(2026, 10, 1);
    private static readonly CaseActor Hr = new("u-1", "A. Sharma", "hrta");
    private static readonly OnboardingTemplate Template = OnboardingTemplate.Default;

    private static OnboardingCase NewCase(int? probationMonths = null) =>
        OnboardingCase.Start(
            new AcceptedOffer("APP-2026-0001", "offer-1", "MRF-2026-0156", "cand-1", Joining, probationMonths),
            Template,
            [
                new(MilestoneKinds.Engagement(21), "Engagement touchpoint (T-21d)", MilestonePhase.PreBoarding, new DateOnly(2026, 9, 10)),
                new(MilestoneKinds.Engagement(7), "Engagement touchpoint (T-7d)", MilestonePhase.PreBoarding, new DateOnly(2026, 9, 24)),
                new(MilestoneKinds.ItProvisioning, "IT / Admin Day-1 setup", MilestonePhase.PreBoarding, new DateOnly(2026, 9, 24)),
            ],
            Now);

    private static void TickAll(OnboardingCase c)
    {
        foreach (var item in c.Checklist.ToList())
        {
            Assert.True(c.SetChecklistItem(item.Key, true, null, Hr, Now).IsSuccess);
        }
    }

    private static void VerifyMandatory(OnboardingCase c)
    {
        foreach (var doc in c.Documents.Where(d => d.Mandatory).ToList())
        {
            Assert.True(c.AttachDocument(doc.Type, new StoredFile($"{doc.Type}.pdf", "application/pdf", 100, $"k/{doc.Type}"), Hr, Now).IsSuccess);
            Assert.True(c.ReviewDocument(doc.Type, true, null, Hr, Now).IsSuccess);
        }
    }

    private static OnboardingCase JoinedCase()
    {
        var c = NewCase();
        TickAll(c);
        c.ClearDomainEvents();
        return c;
    }

    [Fact]
    public void Acceptance_opens_the_case_with_the_Annexure_E_checklist_documents_and_both_phases_planned()
    {
        var c = NewCase();

        Assert.Equal(CaseStatus.PreBoarding, c.Status);
        Assert.Equal(11, c.Checklist.Count);
        Assert.Equal(9, c.Documents.Count);
        Assert.Equal(6, c.Documents.Count(d => d.Mandatory));
        Assert.Equal(OnboardingTemplate.BuiltInVersion, c.RulesVersionId);
        Assert.Equal(Joining.AddMonths(6), c.ProbationEndsOn);

        var instructions = Assert.Single(c.Milestones, m => m.Kind == MilestoneKinds.JoiningInstructions);
        Assert.Equal(MilestoneStatus.Done, instructions.Status);
        Assert.Equal(
            [MilestoneKinds.Engagement(21), MilestoneKinds.Engagement(7), MilestoneKinds.ItProvisioning, MilestoneKinds.CheckIn, MilestoneKinds.Review, MilestoneKinds.ProbationEnd],
            c.Milestones.Where(m => m.Status == MilestoneStatus.Scheduled).Select(m => m.Kind));
        Assert.Equal(Joining.AddDays(30), c.Milestones.Single(m => m.Kind == MilestoneKinds.CheckIn).DueOn);
        Assert.Equal(Joining.AddDays(60), c.Milestones.Single(m => m.Kind == MilestoneKinds.Review).DueOn);
        Assert.IsType<PreBoardingStartedDomainEvent>(Assert.Single(c.DomainEvents));
    }

    [Fact]
    public void The_offers_probation_length_wins_over_the_template()
    {
        var c = NewCase(probationMonths: 3);

        Assert.Equal(3, c.ProbationMonths);
        Assert.Equal(Joining.AddMonths(3), c.Milestones.Single(m => m.Kind == MilestoneKinds.ProbationEnd).DueOn);
    }

    [Fact]
    public void Day1_Ready_fires_once_at_100_percent_and_then_locks_the_checklist()
    {
        var c = NewCase();
        c.ClearDomainEvents();
        var keys = c.Checklist.Select(i => i.Key).ToList();

        foreach (var key in keys.Take(6))
        {
            Assert.True(c.SetChecklistItem(key, true, "ok", Hr, Now).IsSuccess);
        }

        Assert.Equal(55, c.ChecklistPercent);
        Assert.True(c.SetChecklistItem(keys[0], false, null, Hr, Now).IsSuccess);
        Assert.Equal(45, c.ChecklistPercent);
        Assert.Equal("ok", c.Checklist[0].Remarks);
        Assert.Empty(c.DomainEvents);

        TickAll(c);

        Assert.Equal(CaseStatus.Day1Ready, c.Status);
        Assert.Equal(100, c.ChecklistPercent);
        Assert.IsType<Day1ReadyDomainEvent>(Assert.Single(c.DomainEvents));
        Assert.Equal("checklist_locked", c.SetChecklistItem(keys[0], false, null, Hr, Now).Error!.Code);
        Assert.Equal("checklist_item_not_found", NewCase().SetChecklistItem("astrology", true, null, Hr, Now).Error!.Code);
    }

    [Fact]
    public void The_file_completes_only_when_every_mandatory_document_is_verified()
    {
        var c = NewCase();

        var blocked = c.CompleteFile(Hr, Now);
        Assert.Equal("file_incomplete", blocked.Error!.Code);
        Assert.Equal(6, blocked.Error.Fields.Count);
        Assert.All(blocked.Error.Fields, f => Assert.Equal(nameof(DocumentStatus.Missing), f.Code));

        VerifyMandatory(c);
        Assert.True(c.CompleteFile(Hr, Now).IsSuccess);
        Assert.NotNull(c.FileCompletedAt);

        // A new upload of a mandatory document waits for verification again and reopens the file.
        var replaced = c.AttachDocument("identity-proof", new StoredFile("id2.pdf", "application/pdf", 10, "k/new"), Hr, Now);
        Assert.Equal("k/identity-proof", replaced.Value);
        Assert.Null(c.FileCompletedAt);
        Assert.Equal(["identity-proof"], c.MissingDocuments().Select(m => m.Type));
    }

    [Fact]
    public void Reviews_need_an_upload_and_a_rejection_needs_a_note()
    {
        var c = NewCase();

        Assert.Equal("document_not_uploaded", c.ReviewDocument("photographs", true, null, Hr, Now).Error!.Code);
        c.AttachDocument("photographs", new StoredFile("p.jpg", "image/jpeg", 10, "k/p"), Hr, Now);
        Assert.Equal("note_required", c.ReviewDocument("photographs", false, " ", Hr, Now).Error!.Fields[0].Code);
        Assert.True(c.ReviewDocument("photographs", false, "Blurred", Hr, Now).IsSuccess);
        Assert.Equal(DocumentStatus.Rejected, c.Documents.Single(d => d.Type == "photographs").Status);
        Assert.Equal("document_not_found", c.ReviewDocument("horoscope", true, null, Hr, Now).Error!.Code);
    }

    [Fact]
    public void Due_milestones_are_raised_once_and_the_IT_ticket_goes_through_the_adapter()
    {
        var c = NewCase();
        c.ClearDomainEvents();
        var sept24 = new DateOnly(2026, 9, 24);

        Assert.Empty(c.RaiseDueMilestones(new DateOnly(2026, 9, 9), Now));
        var raised = c.RaiseDueMilestones(sept24, Now);
        Assert.Equal([MilestoneKinds.Engagement(21), MilestoneKinds.Engagement(7)], raised.Select(m => m.Kind));
        Assert.Equal(2, c.DomainEvents.OfType<MilestoneDueDomainEvent>().Count());
        Assert.Empty(c.RaiseDueMilestones(sept24, Now));

        var it = c.ProvisioningDue(sept24);
        Assert.NotNull(it);
        Assert.True(c.RecordProvisioning(it.Id, "IT-4412", Now).IsSuccess);
        Assert.Null(c.ProvisioningDue(sept24));
        Assert.Equal("IT-4412", c.Milestones.Single(m => m.Kind == MilestoneKinds.ItProvisioning).TicketRef);
    }

    [Fact]
    public void Milestones_complete_with_notes_but_the_end_of_probation_needs_a_decision()
    {
        var c = NewCase();
        var t21 = c.Milestones.Single(m => m.Kind == MilestoneKinds.Engagement(21));
        var end = c.Milestones.Single(m => m.Kind == MilestoneKinds.ProbationEnd);

        Assert.True(c.CompleteMilestone(t21.Id, " Drop-out risk call: low ", Hr, Now).IsSuccess);
        Assert.Equal("Drop-out risk call: low", t21.Notes);
        Assert.Equal("illegal_transition", c.CompleteMilestone(t21.Id, null, Hr, Now).Error!.Code);
        Assert.Equal("use_probation_decision", c.CompleteMilestone(end.Id, null, Hr, Now).Error!.Code);
        Assert.Equal("milestone_not_found", c.CompleteMilestone(Guid.NewGuid(), null, Hr, Now).Error!.Code);
    }

    [Fact]
    public void Confirmation_needs_Day1_the_end_of_probation_a_complete_file_and_a_cleared_BGV()
    {
        var end = Joining.AddMonths(6);

        Assert.Equal("not_day1_ready", NewCase().DecideProbation(ProbationOutcome.Confirm, null, null, BgvStatus.Cleared, Hr, end, Now).Error!.Code);

        var c = JoinedCase();
        Assert.Equal("probation_not_ended", c.DecideProbation(ProbationOutcome.Confirm, null, null, BgvStatus.Cleared, Hr, end.AddDays(-1), Now).Error!.Code);
        Assert.Equal("file_incomplete", c.DecideProbation(ProbationOutcome.Confirm, null, null, BgvStatus.Cleared, Hr, end, Now).Error!.Code);

        VerifyMandatory(c);
        Assert.Equal("bgv_not_cleared", c.DecideProbation(ProbationOutcome.Confirm, null, null, BgvStatus.Pending, Hr, end, Now).Error!.Code);
        Assert.True(c.DecideProbation(ProbationOutcome.Confirm, "Met every goal", null, BgvStatus.Cleared, Hr, end, Now).IsSuccess);

        Assert.Equal(CaseStatus.Confirmed, c.Status);
        Assert.IsType<EmployeeConfirmedDomainEvent>(Assert.Single(c.DomainEvents));
        Assert.Equal(MilestoneStatus.Done, c.Milestones.Single(m => m.Kind == MilestoneKinds.ProbationEnd).Status);
        Assert.DoesNotContain(c.Milestones, m => m.IsOpen);
        Assert.Equal("onboarding_case_closed", c.DecideProbation(ProbationOutcome.Extend, "again", 2, BgvStatus.Cleared, Hr, end, Now).Error!.Code);
    }

    [Fact]
    public void An_extension_needs_a_reason_and_starts_a_new_review_cycle()
    {
        var c = JoinedCase();
        var end = c.ProbationEndsOn;

        Assert.Equal("reason_required", c.DecideProbation(ProbationOutcome.Extend, " ", 3, BgvStatus.Cleared, Hr, end, Now).Error!.Fields[0].Code);
        Assert.Equal("extension_length", c.DecideProbation(ProbationOutcome.Extend, "Targets missed", 9, BgvStatus.Cleared, Hr, end, Now).Error!.Fields[0].Code);
        Assert.True(c.DecideProbation(ProbationOutcome.Extend, "Targets missed", 3, BgvStatus.Pending, Hr, end, Now).IsSuccess);

        Assert.Equal(2, c.ProbationCycle);
        Assert.Equal(end.AddMonths(3), c.ProbationEndsOn);
        Assert.Equal(CaseStatus.Day1Ready, c.Status);
        var decision = Assert.Single(c.Decisions);
        Assert.Equal((ProbationOutcome.Extend, "Targets missed", 3), (decision.Outcome, decision.Reason, decision.ExtendedByMonths));
        var cycle2 = c.Milestones.Where(m => m.Cycle == 2).ToList();
        Assert.Equal([MilestoneKinds.Review, MilestoneKinds.ProbationEnd], cycle2.Select(m => m.Kind));
        Assert.True(cycle2[0].DueOn > end && cycle2[0].DueOn < c.ProbationEndsOn);
        Assert.IsType<ProbationExtendedDomainEvent>(Assert.Single(c.DomainEvents));
        Assert.Equal("probation_not_ended", c.DecideProbation(ProbationOutcome.Confirm, null, null, BgvStatus.Cleared, Hr, end, Now).Error!.Code);
    }

    [Fact]
    public void A_withdrawn_offer_cancels_the_case_stops_reminders_and_returns_tickets_to_cancel()
    {
        var c = NewCase();
        var it = c.ProvisioningDue(new DateOnly(2026, 9, 24))!;
        c.RecordProvisioning(it.Id, "IT-4412", Now);

        var tickets = c.Cancel("Offer withdrawn", Now);

        Assert.Equal(["IT-4412"], tickets);
        Assert.Equal(CaseStatus.Cancelled, c.Status);
        Assert.DoesNotContain(c.Milestones, m => m.IsOpen);
        Assert.Empty(c.Cancel("again", Now));
        Assert.Equal("onboarding_case_closed", c.SetChecklistItem("id-card", true, null, Hr, Now).Error!.Code);
    }

    [Fact]
    public void BGV_tracks_ignore_older_events_and_keep_an_adverse_outcome()
    {
        var t0 = Now;
        var track = BgvTrack.Start("APP-1", "case-1", BgvStatus.Pending, t0);

        Assert.True(track.Apply(BgvStatus.Cleared, null, t0.AddHours(1)));
        Assert.False(track.Apply(BgvStatus.UnderReview, null, t0.AddMinutes(30)));
        Assert.False(track.Apply(BgvStatus.Pending, null, t0.AddHours(1)));
        Assert.Equal(BgvStatus.Cleared, track.Status);
        Assert.True(track.Apply(BgvStatus.Adverse, null, t0.AddHours(2)));
        Assert.False(track.Apply(BgvStatus.Cleared, null, t0.AddHours(3)));
        Assert.Equal("case-1", track.CaseId);
    }

    [Fact]
    public void State_machines_are_complete_tables()
    {
        Assert.True(CaseTransitions.Table.CanMove(CaseStatus.PreBoarding, CaseStatus.Day1Ready));
        Assert.False(CaseTransitions.Table.CanMove(CaseStatus.PreBoarding, CaseStatus.Confirmed));
        Assert.Empty(CaseTransitions.Table.AllowedFrom(CaseStatus.Confirmed));
        Assert.True(MilestoneTransitions.Table.CanMove(MilestoneStatus.Scheduled, MilestoneStatus.Done));
        Assert.False(MilestoneTransitions.Table.CanMove(MilestoneStatus.Done, MilestoneStatus.Due));
    }
}
