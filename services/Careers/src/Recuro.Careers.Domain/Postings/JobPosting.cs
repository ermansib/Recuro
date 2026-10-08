using Recuro.BuildingBlocks.Domain;

namespace Recuro.Careers.Domain.Postings;

/// <summary>Where a posting stands on the public careers site (RCU-CAR-007).</summary>
public enum PostingStatus
{
    /// <summary>Written by HR-TA, not visible to the public yet.</summary>
    Draft,

    /// <summary>Visible on the public site from <see cref="JobPosting.VisibleFrom"/>.</summary>
    Published,

    /// <summary>Taken down by HR; can be published again.</summary>
    Unpublished,

    /// <summary>The requisition was cancelled or filled. Terminal.</summary>
    Closed,
}

/// <summary>RCU-CAR-007: legal posting moves as data.</summary>
public static class PostingTransitions
{
    public static readonly TransitionTable<PostingStatus> Table = new(new Dictionary<PostingStatus, PostingStatus[]>
    {
        [PostingStatus.Draft] = [PostingStatus.Published, PostingStatus.Closed],
        [PostingStatus.Published] = [PostingStatus.Unpublished, PostingStatus.Closed],
        [PostingStatus.Unpublished] = [PostingStatus.Published, PostingStatus.Closed],
        [PostingStatus.Closed] = [],
    });
}

/// <summary>A chip on the posting card, the frontend's <c>{ text, tone }</c>.</summary>
public sealed record PostingTag(string Text, string Tone);

/// <summary>What HR-TA writes about the role. Everything here is public: no personal data, no CTC.</summary>
public sealed record PostingContent(
    string Title,
    string Location,
    string LocationFilter,
    string Experience,
    string Qualification,
    string? Industry,
    IReadOnlyList<PostingTag> Tags);

/// <summary>Kinds of entries in <see cref="JobPosting.History"/>.</summary>
public enum PostingAction
{
    Drafted,
    Edited,
    Published,
    PublishedEarly,
    Unpublished,
    Closed,
}

/// <summary>One logged change to a posting (RCU-CAR-007: manual publish overrides are logged).</summary>
public sealed record PostingHistoryEntry(PostingAction Action, string By, string? ById, DateTimeOffset At, string? Reason);

/// <summary>Who changed a posting: a person from the token, or a service reacting to an event.</summary>
public sealed record PostingActor(string? Id, string Name);

/// <summary>
/// One requisition's advert on the public careers site. HR-TA writes it, publishes it once sourcing is
/// unlocked, and it closes itself when the requisition is cancelled (RCU-CAR-001/007).
/// </summary>
public sealed class JobPosting : AggregateRoot, ITenantOwned
{
    private readonly List<PostingTag> _tags = [];
    private readonly List<PostingHistoryEntry> _history = [];

    private JobPosting()
    {
    }

    public Guid TenantId { get; private set; }

    /// <summary>Public id, e.g. <c>post-2026-0156</c> for <c>REQ-2026-0156</c>. Never personal data.</summary>
    public string PostingId { get; private set; } = string.Empty;

    public string ReqId { get; private set; } = string.Empty;

    public string Title { get; private set; } = string.Empty;

    public string Location { get; private set; } = string.Empty;

    /// <summary>The value the location filter matches, e.g. <c>Mumbai</c> for <c>HQ Mumbai</c>.</summary>
    public string LocationFilter { get; private set; } = string.Empty;

    public string Experience { get; private set; } = string.Empty;

    public string Qualification { get; private set; } = string.Empty;

    /// <summary>Optional search facet (RCU-CAR-001 <c>industry</c>); not shown on the card.</summary>
    public string? Industry { get; private set; }

    public IReadOnlyList<PostingTag> Tags => _tags;

    public PostingStatus Status { get; private set; }

    /// <summary>The first moment the public sees it: publish time, or the end of the IJP window when that is later.</summary>
    public DateTimeOffset? VisibleFrom { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<PostingHistoryEntry> History => _history;

    public static string PostingIdFor(string reqId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reqId);
        var suffix = reqId.StartsWith("REQ-", StringComparison.OrdinalIgnoreCase) ? reqId[4..] : reqId;
        return $"post-{suffix.ToLowerInvariant()}";
    }

    public static JobPosting Draft(string reqId, PostingContent content, PostingActor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(actor);
        var posting = new JobPosting
        {
            Id = Guid.CreateVersion7(),
            PostingId = PostingIdFor(reqId),
            ReqId = reqId,
            Status = PostingStatus.Draft,
        };
        posting.Apply(content, now);
        posting.Log(PostingAction.Drafted, actor, now, null);
        return posting;
    }

    public bool IsVisibleAt(DateTimeOffset now) => Status == PostingStatus.Published && VisibleFrom <= now;

    /// <summary>HR-TA edits the advert. A closed posting is frozen.</summary>
    public Result Edit(PostingContent content, PostingActor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (Status == PostingStatus.Closed)
        {
            return PostingErrors.Closed(PostingId);
        }

        Apply(content, now);
        Log(PostingAction.Edited, actor, now, null);
        return Result.Success();
    }

    /// <summary>
    /// RCU-CAR-007 / RCU-EMP-001: publish once sourcing is open. The public sees it from
    /// <paramref name="ijpWindowEnd"/> when the internal window is still running, unless an HR Head
    /// opens it early with a justification, which is logged.
    /// </summary>
    public Result Publish(bool sourcingOpen, DateTimeOffset ijpWindowEnd, EarlyRelease? early, PostingActor actor, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(actor);
        if (!sourcingOpen)
        {
            return PostingErrors.SourcingLocked(ReqId);
        }

        var moved = PostingTransitions.Table.EnsureCanMove(Status, PostingStatus.Published, "Posting");
        if (moved.IsFailure)
        {
            return moved;
        }

        if (early is not null && ijpWindowEnd > now)
        {
            if (string.IsNullOrWhiteSpace(early.Justification))
            {
                return PostingErrors.JustificationRequired;
            }

            (Status, VisibleFrom, UpdatedAt) = (PostingStatus.Published, now, now);
            Log(PostingAction.PublishedEarly, actor, now, early.Justification.Trim());
            return Result.Success();
        }

        (Status, VisibleFrom, UpdatedAt) = (PostingStatus.Published, ijpWindowEnd > now ? ijpWindowEnd : now, now);
        Log(PostingAction.Published, actor, now, null);
        return Result.Success();
    }

    public Result Unpublish(string reason, PostingActor actor, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return PostingErrors.ReasonRequired;
        }

        var moved = PostingTransitions.Table.EnsureCanMove(Status, PostingStatus.Unpublished, "Posting");
        if (moved.IsFailure)
        {
            return moved;
        }

        (Status, VisibleFrom, UpdatedAt) = (PostingStatus.Unpublished, null, now);
        Log(PostingAction.Unpublished, actor, now, reason.Trim());
        return Result.Success();
    }

    /// <summary>The requisition is gone (cancelled or filled): the public site stops showing it at once.</summary>
    public void Close(string reason, PostingActor actor, DateTimeOffset now)
    {
        if (Status == PostingStatus.Closed)
        {
            return;
        }

        (Status, VisibleFrom, UpdatedAt) = (PostingStatus.Closed, null, now);
        Log(PostingAction.Closed, actor, now, reason);
    }

    private void Apply(PostingContent content, DateTimeOffset now)
    {
        Title = content.Title.Trim();
        Location = content.Location.Trim();
        LocationFilter = content.LocationFilter.Trim();
        Experience = content.Experience.Trim();
        Qualification = content.Qualification.Trim();
        Industry = string.IsNullOrWhiteSpace(content.Industry) ? null : content.Industry.Trim();
        _tags.Clear();
        _tags.AddRange(content.Tags);
        UpdatedAt = now;
    }

    private void Log(PostingAction action, PostingActor actor, DateTimeOffset now, string? reason) =>
        _history.Add(new PostingHistoryEntry(action, actor.Name, actor.Id, now, reason));
}

/// <summary>An HR Head's request to open the public site before the IJP window ends (RCU-EMP-001, FRD §9.2.2).</summary>
public sealed record EarlyRelease(string Justification);
