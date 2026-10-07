using Recuro.Candidate.Domain.Candidates;
using CandidateEntity = Recuro.Candidate.Domain.Candidates.Candidate;

namespace Recuro.Candidate.UnitTests;

public class CandidateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 10, 0, 0, TimeSpan.Zero);
    private static readonly Consent Privacy = new(ConsentType.DataPrivacy, "v1", Now, "careers-portal");

    private static CandidateEntity NewCandidate(params Consent[] consents) =>
        CandidateEntity.Create(
            new CandidateDetails("Rahul Mehta", "rahul.mehta@email.example", "+91 98200 40001", 8, "8 yrs · BFSI Credit", 19, 21, 30),
            new SourceAttribution(CandidateSource.Portal, null, null, null, null),
            consents.Length == 0 ? [Privacy] : consents,
            new ContactFingerprints("e", "p"),
            Now).Value;

    [Fact]
    public void Creation_needs_the_privacy_consent()
    {
        var result = CandidateEntity.Create(
            new CandidateDetails("A", "a@b.example", null, 1, "", null, null, null),
            new SourceAttribution(CandidateSource.Ijp, null, null, null, null),
            [new Consent(ConsentType.ConflictOfInterest, "v1", Now, "x")],
            new ContactFingerprints("e", null),
            Now);

        Assert.True(result.IsFailure);
        Assert.Equal("consents", result.Error!.Fields.Single().Field);
    }

    [Fact]
    public void Creation_raises_an_event_with_ids_only()
    {
        var candidate = NewCandidate();

        var created = Assert.IsType<CandidateCreatedDomainEvent>(Assert.Single(candidate.DomainEvents));
        Assert.Equal(candidate.Id, created.CandidateId);
        Assert.Equal(RetentionStatus.None, candidate.RetentionStatus);
    }

    [Fact]
    public void An_open_application_keeps_the_candidate_active()
    {
        var candidate = NewCandidate();
        candidate.TrackApplication("APP-1");
        candidate.TrackApplication("APP-2");
        candidate.CloseApplication("APP-1", ApplicationOutcome.Unsuccessful, new DateOnly(2027, 10, 7));

        Assert.Equal(RetentionStatus.Active, candidate.RetentionStatus);
        Assert.Null(candidate.RetainUntil);
    }

    [Fact]
    public void All_applications_unsuccessful_keeps_data_until_the_latest_retention_date()
    {
        var candidate = NewCandidate();
        candidate.TrackApplication("APP-1");
        candidate.CloseApplication("APP-1", ApplicationOutcome.Unsuccessful, new DateOnly(2027, 1, 1));
        candidate.CloseApplication("APP-2", ApplicationOutcome.Unsuccessful, new DateOnly(2027, 6, 1));

        Assert.Equal(RetentionStatus.Unsuccessful, candidate.RetentionStatus);
        Assert.Equal(new DateOnly(2027, 6, 1), candidate.RetainUntil);
        Assert.False(candidate.IsDueForPurge(new DateOnly(2027, 6, 1)));
        Assert.True(candidate.IsDueForPurge(new DateOnly(2027, 6, 2)));
    }

    [Fact]
    public void A_hire_is_never_due_for_purge()
    {
        var candidate = NewCandidate();
        candidate.CloseApplication("APP-1", ApplicationOutcome.Unsuccessful, new DateOnly(2020, 1, 1));
        candidate.CloseApplication("APP-2", ApplicationOutcome.Hired, null);

        Assert.Equal(RetentionStatus.Hired, candidate.RetentionStatus);
        Assert.False(candidate.IsDueForPurge(new DateOnly(2030, 1, 1)));
    }

    [Fact]
    public void Purge_scrubs_personal_data_and_keeps_a_statistics_shell()
    {
        var candidate = NewCandidate();
        candidate.AttachResume(new ResumeFile("cv.pdf", "application/pdf", 10, "t/c/f", Now));
        candidate.CloseApplication("APP-1", ApplicationOutcome.Unsuccessful, new DateOnly(2026, 1, 1));
        candidate.ClearDomainEvents();

        var result = candidate.Purge(Now);

        Assert.True(result.IsSuccess);
        Assert.Equal(string.Empty, candidate.Name);
        Assert.Equal(string.Empty, candidate.Email);
        Assert.Null(candidate.Phone);
        Assert.Null(candidate.EmailFingerprint);
        Assert.Null(candidate.CurrentCtc);
        Assert.Null(candidate.Resume);
        Assert.Empty(candidate.Consents);
        Assert.Equal(CandidateSource.Portal, candidate.Attribution.Source);
        Assert.Equal(8, candidate.ExperienceYears);
        var purged = Assert.IsType<CandidatePurgedDomainEvent>(Assert.Single(candidate.DomainEvents));
        Assert.Equal("t/c/f", purged.ResumeStorageKey);
    }

    [Fact]
    public void Legal_hold_blocks_purge()
    {
        var candidate = NewCandidate();
        candidate.CloseApplication("APP-1", ApplicationOutcome.Unsuccessful, new DateOnly(2026, 1, 1));
        candidate.PlaceLegalHold("Grievance G-12");

        Assert.Equal(CandidateErrors.OnLegalHold, candidate.Purge(Now).Error);
        Assert.False(candidate.IsDueForPurge(new DateOnly(2027, 1, 1)));
    }

    [Fact]
    public void Purge_before_retention_ends_is_refused()
    {
        var candidate = NewCandidate();
        candidate.CloseApplication("APP-1", ApplicationOutcome.Unsuccessful, new DateOnly(2027, 1, 1));

        Assert.Equal(CandidateErrors.NotDueForPurge, candidate.Purge(Now).Error);
    }

    [Fact]
    public void A_purged_candidate_ignores_later_application_events()
    {
        var candidate = NewCandidate();
        candidate.CloseApplication("APP-1", ApplicationOutcome.Unsuccessful, new DateOnly(2026, 1, 1));
        candidate.Purge(Now);

        candidate.TrackApplication("APP-9");

        Assert.Single(candidate.Applications);
        Assert.Equal(CandidateErrors.AlreadyPurged, candidate.Purge(Now).Error);
    }

    [Fact]
    public void Replacing_a_resume_returns_the_old_file_to_delete()
    {
        var candidate = NewCandidate();
        candidate.AttachResume(new ResumeFile("a.pdf", "application/pdf", 1, "old", Now));

        var result = candidate.AttachResume(new ResumeFile("b.pdf", "application/pdf", 1, "new", Now));

        Assert.Equal("old", result.Value);
        Assert.Equal("new", candidate.Resume!.StorageKey);
    }
}
