using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.Employee.Api.Endpoints;

/// <summary>FRD §3.2 for the employee portal (S-16).</summary>
internal static class EmployeePolicies
{
    /// <summary>Employees browse IJP openings, apply, refer and see their own records. HR staff may browse too.</summary>
    public const string Browse = "employee.ijp.read";

    /// <summary>RCU-EMP-002/003/005: actions on the caller's own behalf.</summary>
    public const string Self = "employee.self";

    /// <summary>RCU-EMP-001: HR-TA posts internal openings.</summary>
    public const string ManageIjp = "employee.ijp.manage";

    public static IServiceCollection AddEmployeePolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddRolePolicy(Browse, [RecuroRoles.Employee, .. RecuroRoles.HrStaff])
            .AddRolePolicy(Self, RecuroRoles.Employee)
            .AddRolePolicy(ManageIjp, RecuroRoles.HrTa);
        return services;
    }
}
