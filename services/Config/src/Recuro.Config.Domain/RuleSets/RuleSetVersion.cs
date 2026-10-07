using Recuro.BuildingBlocks.Domain;

namespace Recuro.Config.Domain.RuleSets;

public enum VersionStatus
{
    /// <summary>Proposed, editable by its proposer, not used by any resolution.</summary>
    Draft,

    /// <summary>Approved by a second person. Immutable; in force from <see cref="RuleSetVersion.EffectiveFrom"/>.</summary>
    Active,

    /// <summary>Turned down. Immutable.</summary>
    Rejected,
}

/// <summary>
/// One version of one rules matrix for one tenant (RCU-CFG-001). Policy changes are governed by dual
/// approval: a draft becomes active only when a different person approves it, and an active version
/// never changes again. A version is in force from its <see cref="EffectiveFrom"/> until the next
/// active version's, so history is never rewritten (RCU-CFG-003).
/// </summary>
public sealed class RuleSetVersion : AggregateRoot, ITenantOwned
{
    public const string SystemActor = "system";

    public static readonly TransitionTable<VersionStatus> Transitions = new(new Dictionary<VersionStatus, VersionStatus[]>
    {
        [VersionStatus.Draft] = [VersionStatus.Active, VersionStatus.Rejected],
        [VersionStatus.Active] = [],
        [VersionStatus.Rejected] = [],
    });

    private RuleSetVersion()
    {
    }

    public Guid TenantId { get; private set; }

    public MatrixType MatrixType { get; private set; }

    /// <summary>1, 2, 3… per tenant and matrix type.</summary>
    public int Number { get; private set; }

    public VersionStatus Status { get; private set; }

    /// <summary>The matrix as camelCase JSON, validated against its type before it is stored.</summary>
    public string Content { get; private set; } = string.Empty;

    public DateTimeOffset EffectiveFrom { get; private set; }

    public string? Note { get; private set; }

    public string ProposedById { get; private set; } = string.Empty;

    public string ProposedByName { get; private set; } = string.Empty;

    public DateTimeOffset ProposedAt { get; private set; }

    public string? DecidedById { get; private set; }

    public string? DecidedByName { get; private set; }

    public DateTimeOffset? DecidedAt { get; private set; }

    public string? RejectionReason { get; private set; }

    public static RuleSetVersion Propose(
        MatrixType type, int number, string content, DateTimeOffset effectiveFrom, string? note, string proposedById, string proposedByName, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposedById);
        ArgumentOutOfRangeException.ThrowIfLessThan(number, 1);
        return new RuleSetVersion
        {
            Id = Guid.CreateVersion7(),
            MatrixType = type,
            Number = number,
            Status = VersionStatus.Draft,
            Content = content,
            EffectiveFrom = effectiveFrom.ToUniversalTime(),
            Note = note,
            ProposedById = proposedById,
            ProposedByName = string.IsNullOrWhiteSpace(proposedByName) ? proposedById : proposedByName,
            ProposedAt = now,
        };
    }

    /// <summary>
    /// The FRD §5 seed for a new tenant: version 1, active from <paramref name="effectiveFrom"/>. Seeding
    /// is a system bootstrap, not a policy change, so it skips dual approval.
    /// </summary>
    public static RuleSetVersion Seed(MatrixType type, string content, DateTimeOffset effectiveFrom, DateTimeOffset now)
    {
        var version = Propose(type, 1, content, effectiveFrom, "FRD §5 seed", SystemActor, SystemActor, now);
        version.Status = VersionStatus.Active;
        version.DecidedById = SystemActor;
        version.DecidedByName = SystemActor;
        version.DecidedAt = now;
        return version;
    }

    /// <summary>The proposer revises their draft. Active and rejected versions never change.</summary>
    public Result Revise(string content, DateTimeOffset effectiveFrom, string? note, string editorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);
        if (Status != VersionStatus.Draft)
        {
            return ConfigErrors.Locked(Status);
        }

        if (editorId != ProposedById)
        {
            return ConfigErrors.NotProposer;
        }

        Content = content;
        EffectiveFrom = effectiveFrom.ToUniversalTime();
        Note = note;
        return Result.Success();
    }

    /// <summary>
    /// Second-person approval. A version can't take effect in the past: if its effective date has
    /// passed, it takes effect now, so resolutions already made stay true.
    /// </summary>
    public Result Approve(string approverId, string approverName, DateTimeOffset now)
    {
        var move = Transitions.EnsureCanMove(Status, VersionStatus.Active, "Rule set version");
        if (move.IsFailure)
        {
            return move;
        }

        if (approverId == ProposedById)
        {
            return ConfigErrors.SecondApproverRequired;
        }

        if (EffectiveFrom < now)
        {
            EffectiveFrom = now;
        }

        Status = VersionStatus.Active;
        DecidedById = approverId;
        DecidedByName = string.IsNullOrWhiteSpace(approverName) ? approverId : approverName;
        DecidedAt = now;
        Raise(new RuleSetActivatedDomainEvent(Id, MatrixType, Number, EffectiveFrom));
        return Result.Success();
    }

    public Result Reject(string deciderId, string deciderName, string reason, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var move = Transitions.EnsureCanMove(Status, VersionStatus.Rejected, "Rule set version");
        if (move.IsFailure)
        {
            return move;
        }

        Status = VersionStatus.Rejected;
        DecidedById = deciderId;
        DecidedByName = string.IsNullOrWhiteSpace(deciderName) ? deciderId : deciderName;
        DecidedAt = now;
        RejectionReason = reason;
        return Result.Success();
    }
}

/// <summary>A version was approved and is (or will be) in force.</summary>
public sealed record RuleSetActivatedDomainEvent(Guid VersionId, MatrixType MatrixType, int Number, DateTimeOffset EffectiveFrom) : IDomainEvent;

public static class ConfigErrors
{
    public static readonly Error NotProposer =
        Error.Forbidden("not_proposer", "Only the person who proposed this draft can change it.");

    public static readonly Error SecondApproverRequired =
        Error.Forbidden("second_approver_required", "Rule changes need a second approver: you can't approve your own proposal.");

    public static Error Locked(VersionStatus status) =>
        Error.Conflict("version_locked", $"This version is {status} and can no longer change. Propose a new version instead.");
}
