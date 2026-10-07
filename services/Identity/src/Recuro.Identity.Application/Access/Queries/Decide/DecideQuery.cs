using FluentValidation;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Identity.Application.Abstractions;
using Recuro.Identity.Domain.Access;

namespace Recuro.Identity.Application.Access.Queries.Decide;

/// <summary>RCU-AUT-003: the policy decision point. Default deny.</summary>
public sealed record DecideQuery(
    string ActorId,
    IReadOnlyList<string> ActorRoles,
    string Action,
    string? ResourceType,
    string? ResourceId,
    IReadOnlyList<string> AssigneeIds,
    bool MfaVerified) : IQuery<DecisionDto>;

/// <summary>
/// The decision. Callers may cache it for <see cref="TtlSeconds"/> (at most 60 s, RCU-AUT-003), keyed by
/// tenant, actor, action and resource.
/// </summary>
public sealed record DecisionDto(bool Allow, IReadOnlyList<string> Reasons, string PolicyVersion, int TtlSeconds)
{
    public const int CacheSeconds = 60;
}

internal sealed class DecideQueryValidator : AbstractValidator<DecideQuery>
{
    public DecideQueryValidator()
    {
        RuleFor(q => q.ActorId).NotEmpty().MaximumLength(100);
        RuleFor(q => q.ActorRoles).NotNull();
        RuleFor(q => q.Action).NotEmpty().MaximumLength(100);
        RuleFor(q => q.AssigneeIds).NotNull().Must(ids => ids.Count <= 50).WithMessage("At most 50 assignees.");
    }
}

internal sealed class DecideQueryHandler(IAccessPolicyProvider policies) : IQueryHandler<DecideQuery, DecisionDto>
{
    public async Task<Result<DecisionDto>> Handle(DecideQuery query, CancellationToken ct)
    {
        var policy = await policies.GetAsync(ct);
        var decision = policy.Decide(new AccessRequest(query.ActorId, query.ActorRoles, query.Action, query.AssigneeIds, query.MfaVerified));
        return new DecisionDto(decision.Allow, decision.Reasons, policy.Version, DecisionDto.CacheSeconds);
    }
}
