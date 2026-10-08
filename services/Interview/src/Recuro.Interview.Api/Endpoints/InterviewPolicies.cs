using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.Interview.Api.Endpoints;

/// <summary>RBAC from FRD §3.2 (frontend auth/permissions.ts), enforced here, not just hidden in the UI.</summary>
internal static class InterviewPolicies
{
    /// <summary>RCU-INT-001/007: HR-TA schedules and reschedules rounds.</summary>
    public const string Schedule = "interviews.schedule";

    /// <summary>HR staff read every round and the selection summary; services read them too.</summary>
    public const string Read = "interviews.read";

    /// <summary>RCU-INT-006: HR-TA submits the selection for ratification.</summary>
    public const string Select = "interviews.select";

    public static IServiceCollection AddInterviewPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddRolePolicy(Schedule, RecuroRoles.HrTa)
            .AddRolePolicy(Read, [.. RecuroRoles.HrStaff, RecuroRoles.Service])
            .AddRolePolicy(Select, RecuroRoles.HrTa);
        return services;
    }
}
