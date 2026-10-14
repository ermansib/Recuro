using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Domain;
using Recuro.Onboarding.Application.Abstractions;
using Recuro.Onboarding.Domain.Cases;

namespace Recuro.Onboarding.Application.Cases;

/// <summary>
/// Who decides a probation (RCU-ONB-005): the head of the joiner's department, found through Identity
/// in the case's tenant. The department is the one recorded on the case, else the reporting manager's.
/// When there is no department or it has no head, HR Head decides. Both the hierarchy and the HOD role
/// are tenant data kept in Identity, so Onboarding holds no names.
/// </summary>
public sealed record ProbationRoute(string? Department, Person? Head)
{
    public const string HodRole = "hod";
    public const string HrHeadRole = "hrhead";

    /// <summary>The role the decision is recorded under: <c>hod</c> when a head is on record, else <c>hrhead</c>.</summary>
    public string DeciderRole => Head is null ? HrHeadRole : HodRole;
}

internal sealed class ProbationDecider(IPeopleDirectory people)
{
    public async Task<ProbationRoute> RouteAsync(OnboardingCase onboardingCase, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(onboardingCase);
        var department = onboardingCase.Department;
        if (department is null && onboardingCase.ReportingManagerId is { } managerId)
        {
            var manager = await people.FindAsync(managerId, ct);
            department = string.IsNullOrWhiteSpace(manager?.Department) ? null : manager.Department;
        }

        var head = department is null ? null : await people.FindDepartmentHeadAsync(department, ct);
        return new ProbationRoute(department, head);
    }

    /// <summary>Allows the department's head, or HR Head when the department has none.</summary>
    public static Result<CaseActor> Authorize(ProbationRoute route, ICurrentUser caller)
    {
        ArgumentNullException.ThrowIfNull(route);
        ArgumentNullException.ThrowIfNull(caller);
        var actor = new CaseActor(caller.UserId, caller.Name ?? caller.UserId ?? "unknown", route.DeciderRole);
        var allowed = route.Head is { } head
            ? caller.IsInRole(ProbationRoute.HodRole) && string.Equals(caller.UserId, head.Id, StringComparison.Ordinal)
            : caller.IsInRole(ProbationRoute.HrHeadRole);
        if (allowed)
        {
            return actor;
        }

        return route.Head is null ? OnboardingErrors.HrHeadDecides : OnboardingErrors.NotDepartmentHead(route.Department!);
    }
}
