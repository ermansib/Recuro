using Recuro.BuildingBlocks.Domain;

namespace Recuro.Candidate.Domain.Candidates;

/// <summary>What HR or an intake flow knows about a new candidate.</summary>
public sealed record CandidateDetails(
    string Name,
    string Email,
    string? Phone,
    decimal ExperienceYears,
    string Summary,
    decimal? CurrentCtc,
    decimal? ExpectedCtc,
    int? NoticeDays);

/// <summary>
/// Keyed hashes of the normalised email and phone (blind indexes). They find duplicates without
/// decrypting anyone's contact details (RCU-CND-002).
/// </summary>
public sealed record ContactFingerprints(string Email, string? Phone);

/// <summary>
/// One person, once per tenant (RCU-CND-001..005). Personal fields are encrypted at rest by persistence;
/// the aggregate itself never sees ciphertext. After the retention period an unsuccessful candidate is
/// anonymised: personal data is scrubbed and a shell with non-personal facts stays for statistics.
/// </summary>
public sealed class Candidate : AggregateRoot, ITenantOwned
{
    private readonly List<Consent> _consents = [];
    private readonly List<CandidateApplication> _applications = [];

    private Candidate()
    {
    }

    public Guid TenantId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string Email { get; private set; } = string.Empty;

    public string? Phone { get; private set; }

    public string? EmailFingerprint { get; private set; }

    public string? PhoneFingerprint { get; private set; }

    public decimal ExperienceYears { get; private set; }

    public string Summary { get; private set; } = string.Empty;

    public decimal? CurrentCtc { get; private set; }

    public decimal? ExpectedCtc { get; private set; }

    public int? NoticeDays { get; private set; }

    public SourceAttribution Attribution { get; private set; } = null!;

    public IReadOnlyList<Consent> Consents => _consents;

    public IReadOnlyList<CandidateApplication> Applications => _applications;

    public ResumeFile? Resume { get; private set; }

    public bool LegalHold { get; private set; }

    public string? LegalHoldReason { get; private set; }

    public RetentionStatus RetentionStatus { get; private set; }

    /// <summary>When <see cref="RetentionStatus"/> is Unsuccessful: the last day personal data may be kept.</summary>
    public DateOnly? RetainUntil { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? PurgedAt { get; private set; }

    public bool IsPurged => PurgedAt is not null;

    /// <summary>Creates a candidate. Fails when a required consent is missing (FRD §14).</summary>
    public static Result<Candidate> Create(
        CandidateDetails details,
        SourceAttribution attribution,
        IReadOnlyList<Consent> consents,
        ContactFingerprints fingerprints,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(details);
        ArgumentNullException.ThrowIfNull(attribution);
        ArgumentNullException.ThrowIfNull(consents);
        ArgumentNullException.ThrowIfNull(fingerprints);

        var missing = ConsentPolicy.Missing(attribution.Source, consents);
        if (missing.Count > 0)
        {
            return CandidateErrors.MissingConsent(missing);
        }

        var candidate = new Candidate
        {
            Id = Guid.CreateVersion7(),
            Name = details.Name.Trim(),
            Email = details.Email.Trim(),
            Phone = string.IsNullOrWhiteSpace(details.Phone) ? null : details.Phone.Trim(),
            EmailFingerprint = fingerprints.Email,
            PhoneFingerprint = fingerprints.Phone,
            ExperienceYears = details.ExperienceYears,
            Summary = details.Summary.Trim(),
            CurrentCtc = details.CurrentCtc,
            ExpectedCtc = details.ExpectedCtc,
            NoticeDays = details.NoticeDays,
            Attribution = attribution,
            CreatedAt = now,
            RetentionStatus = RetentionStatus.None,
        };
        candidate._consents.AddRange(consents);
        candidate.Raise(new CandidateCreatedDomainEvent(candidate.Id, attribution.Source));
        return candidate;
    }

    /// <summary>Records a consent given later, e.g. the COI declaration at a second application.</summary>
    public void AddConsent(Consent consent)
    {
        ArgumentNullException.ThrowIfNull(consent);
        if (!IsPurged && !_consents.Contains(consent))
        {
            _consents.Add(consent);
        }
    }

    /// <summary>The candidate applied (pipeline.application.created). Idempotent.</summary>
    public void TrackApplication(string appId)
    {
        if (IsPurged || _applications.Any(a => a.AppId == appId))
        {
            return;
        }

        _applications.Add(new CandidateApplication(appId));
        RecomputeRetention();
    }

    /// <summary>An application ended (final rejection, withdrawal or hire).</summary>
    public void CloseApplication(string appId, ApplicationOutcome outcome, DateOnly? retainUntil)
    {
        if (IsPurged)
        {
            return;
        }

        var application = _applications.FirstOrDefault(a => a.AppId == appId);
        if (application is null)
        {
            application = new CandidateApplication(appId);
            _applications.Add(application);
        }

        application.Close(outcome, retainUntil);
        RecomputeRetention();
    }

    public Result<string?> AttachResume(ResumeFile resume)
    {
        ArgumentNullException.ThrowIfNull(resume);
        if (IsPurged)
        {
            return CandidateErrors.AlreadyPurged;
        }

        var previous = Resume?.StorageKey;
        Resume = resume;
        return previous;
    }

    public void PlaceLegalHold(string reason)
    {
        LegalHold = true;
        LegalHoldReason = reason.Trim();
    }

    public void ReleaseLegalHold()
    {
        LegalHold = false;
        LegalHoldReason = null;
    }

    /// <summary>True when every application ended unsuccessfully and the retention period is over.</summary>
    public bool IsDueForPurge(DateOnly today) =>
        !IsPurged && !LegalHold && RetentionStatus == RetentionStatus.Unsuccessful && RetainUntil is { } until && until < today;

    /// <summary>
    /// RCU-CND-003: irreversibly scrubs personal data. Source, experience and dates stay as a shell for
    /// statistics. Legal holds are respected.
    /// </summary>
    public Result Purge(DateTimeOffset now)
    {
        if (IsPurged)
        {
            return CandidateErrors.AlreadyPurged;
        }

        if (LegalHold)
        {
            return CandidateErrors.OnLegalHold;
        }

        if (!IsDueForPurge(DateOnly.FromDateTime(now.UtcDateTime)))
        {
            return CandidateErrors.NotDueForPurge;
        }

        var resumeKey = Resume?.StorageKey;
        Name = string.Empty;
        Email = string.Empty;
        Phone = null;
        EmailFingerprint = null;
        PhoneFingerprint = null;
        Summary = string.Empty;
        CurrentCtc = null;
        ExpectedCtc = null;
        NoticeDays = null;
        Attribution = Attribution with { SourceRef = null, ReferrerId = null };
        Resume = null;
        _consents.Clear();
        PurgedAt = now;
        Raise(new CandidatePurgedDomainEvent(Id, resumeKey));
        return Result.Success();
    }

    private void RecomputeRetention()
    {
        if (_applications.Any(a => a.Outcome == ApplicationOutcome.Hired))
        {
            (RetentionStatus, RetainUntil) = (RetentionStatus.Hired, null);
        }
        else if (_applications.Any(a => a.Outcome == ApplicationOutcome.Active))
        {
            (RetentionStatus, RetainUntil) = (RetentionStatus.Active, null);
        }
        else if (_applications.Count > 0)
        {
            (RetentionStatus, RetainUntil) = (RetentionStatus.Unsuccessful, _applications.Max(a => a.RetainUntil));
        }
        else
        {
            (RetentionStatus, RetainUntil) = (RetentionStatus.None, null);
        }
    }
}
