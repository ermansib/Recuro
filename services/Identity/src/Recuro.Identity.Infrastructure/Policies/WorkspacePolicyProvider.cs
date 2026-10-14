using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.Identity.Application.Abstractions;
using Recuro.Identity.Domain.Access;
using Recuro.Identity.Domain.Masking;

namespace Recuro.Identity.Infrastructure.Policies;

/// <summary>
/// The policy and masking map in force for the current workspace: the seeded FRD §3.2 data with the
/// workspace's own roles (configured in the admin portal) applied. Without workspace roles, the seeded data.
/// </summary>
internal sealed class WorkspacePolicyProvider(ITenantContext tenant, IWorkspaceAccessSource workspaces) : IAccessPolicyProvider, IMaskingMapProvider
{
    private static readonly AccessPolicy DefaultPolicy = DefaultAccessPolicy.Create();
    private static readonly MaskingMap DefaultMasking = DefaultMaskingMap.Create();

    async Task<AccessPolicy> IAccessPolicyProvider.GetAsync(CancellationToken ct) =>
        await AccessAsync(ct) is { } access ? access.ApplyTo(DefaultPolicy) : DefaultPolicy;

    async Task<MaskingMap> IMaskingMapProvider.GetAsync(CancellationToken ct) =>
        await AccessAsync(ct) is { } access ? access.ApplyTo(DefaultMasking) : DefaultMasking;

    private async Task<WorkspaceAccess?> AccessAsync(CancellationToken ct) =>
        tenant.TenantId is { } id ? await workspaces.GetAsync(id, ct) : null;
}
