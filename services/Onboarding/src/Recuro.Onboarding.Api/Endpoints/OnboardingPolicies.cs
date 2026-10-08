using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.Onboarding.Api.Endpoints;

/// <summary>FRD §3.2 for S-13 Onboarding: HR runs it; MD/CEO, employees and candidates have no access.</summary>
internal static class OnboardingPolicies
{
    /// <summary>HR-TA and HR Head read cases; other services (the gateway BFF, reporting) read with a service token.</summary>
    public const string Read = "onboarding.read";

    /// <summary>RCU-ONB-002/003/004: "HR roles only" tick the checklist, handle documents and complete milestones.</summary>
    public const string Operate = "onboarding.operate";

    /// <summary>
    /// RCU-ONB-005: the probation decision. The FRD gives it to the HOD; Recuro has no HOD role yet
    /// (Identity's HOD lookup is pending), so HR Head records it.
    /// </summary>
    public const string Decide = "onboarding.decide";

    public static IServiceCollection AddOnboardingPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddRolePolicy(Read, RecuroRoles.HrTa, RecuroRoles.HrHead, RecuroRoles.Service)
            .AddRolePolicy(Operate, RecuroRoles.HrTa, RecuroRoles.HrHead)
            .AddRolePolicy(Decide, RecuroRoles.HrHead);
        return services;
    }
}
