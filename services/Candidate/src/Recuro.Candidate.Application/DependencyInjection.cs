using Microsoft.Extensions.DependencyInjection;
using Recuro.BuildingBlocks.Application;
using Recuro.Candidate.Application.Candidates.Masking;

namespace Recuro.Candidate.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddCandidateApplication(this IServiceCollection services) =>
        services.AddRecuroApplication(typeof(DependencyInjection).Assembly)
            .AddScoped<ICallerMask, CallerMask>();
}
