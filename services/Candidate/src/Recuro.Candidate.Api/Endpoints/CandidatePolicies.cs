using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.Candidate.Api.Endpoints;

/// <summary>FRD §3.2 for candidate data. Masking per role happens in the use cases on top of these.</summary>
internal static class CandidatePolicies
{
    /// <summary>HR-TA logs candidates; intake services (careers, IJP, referrals) create them with a service token.</summary>
    public const string Create = "candidate.create";

    /// <summary>HR staff read candidates (MD/CEO masked); services read them unmasked.</summary>
    public const string Read = "candidate.read";

    /// <summary>The CV is full personal data: HR-TA and HR Head only.</summary>
    public const string ReadResume = "candidate.resume.read";

    /// <summary>Retention governance (legal hold, purge): HR Head as the DPO's delegate.</summary>
    public const string Govern = "candidate.govern";

    /// <summary>Saga compensation (tombstone) by intake services only; people use retention governance.</summary>
    public const string Compensate = "candidate.compensate";

    public static IServiceCollection AddCandidatePolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddRolePolicy(Create, RecuroRoles.HrTa, RecuroRoles.Service)
            .AddRolePolicy(Read, RecuroRoles.HrTa, RecuroRoles.HrHead, RecuroRoles.MdCeo, RecuroRoles.Service)
            .AddRolePolicy(ReadResume, RecuroRoles.HrTa, RecuroRoles.HrHead, RecuroRoles.Service)
            .AddRolePolicy(Govern, RecuroRoles.HrHead)
            .AddRolePolicy(Compensate, RecuroRoles.Service);
        return services;
    }
}
