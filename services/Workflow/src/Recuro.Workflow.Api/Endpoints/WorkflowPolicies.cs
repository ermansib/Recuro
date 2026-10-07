using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.Workflow.Api.Endpoints;

/// <summary>RBAC from FRD §3.2: the approvals inbox is for HR staff; only services (and HR staff acting through them) open workflows.</summary>
internal static class WorkflowPolicies
{
    /// <summary>S-04: HR-TA, HR Head and MD/CEO see their own inbox; tasks are filtered to the caller's roles.</summary>
    public const string Approvals = "approvals.use";

    /// <summary>RCU-WFL-001: domain services start and withdraw workflows (on behalf of the signed-in user for now).</summary>
    public const string Manage = "workflows.manage";

    public static IServiceCollection AddWorkflowPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddRolePolicy(Approvals, RecuroRoles.HrStaff)
            .AddRolePolicy(Manage, [.. RecuroRoles.HrStaff, RecuroRoles.Service]);
        return services;
    }
}
