using Recuro.BuildingBlocks.Web.Auth;

namespace Recuro.Pipeline.Api.Endpoints;

/// <summary>FRD §3.2 for the pipeline, the same matrix as the frontend's <c>pipeline.move</c> and <c>candidate.log</c>.</summary>
internal static class PipelinePolicies
{
    /// <summary>HR staff see the board (MD/CEO read-only, masked by the Candidate service); services read applications.</summary>
    public const string Read = "pipeline.read";

    /// <summary>HR-TA logs applications; intake services (careers, IJP, referrals) create them with a service token.</summary>
    public const string Create = "pipeline.create";

    /// <summary>RCU-PPL-002: HR-TA and HR Head move and reject. MD/CEO gets 403.</summary>
    public const string Move = "pipeline.move";

    public static IServiceCollection AddPipelinePolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddRolePolicy(Read, RecuroRoles.HrTa, RecuroRoles.HrHead, RecuroRoles.MdCeo, RecuroRoles.Service)
            .AddRolePolicy(Create, RecuroRoles.HrTa, RecuroRoles.Service)
            .AddRolePolicy(Move, RecuroRoles.HrTa, RecuroRoles.HrHead);
        return services;
    }
}
