using Recuro.Identity.Application.Abstractions;
using Recuro.Identity.Domain.Access;
using Recuro.Identity.Domain.Masking;

namespace Recuro.Identity.Infrastructure.Policies;

/// <summary>
/// Serves the seeded policy and masking map (FRD §3.2) to every tenant. Per-tenant versions plug in
/// behind the same interfaces later, without touching the use cases.
/// </summary>
internal sealed class DefaultPolicyProvider : IAccessPolicyProvider, IMaskingMapProvider
{
    private static readonly AccessPolicy Policy = DefaultAccessPolicy.Create();
    private static readonly MaskingMap Masking = DefaultMaskingMap.Create();

    Task<AccessPolicy> IAccessPolicyProvider.GetAsync(CancellationToken ct) => Task.FromResult(Policy);

    Task<MaskingMap> IMaskingMapProvider.GetAsync(CancellationToken ct) => Task.FromResult(Masking);
}
