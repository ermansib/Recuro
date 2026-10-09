using Microsoft.Extensions.Logging;
using Recuro.Admin.Application.Abstractions;
using Recuro.Admin.Domain.Common;

namespace Recuro.Admin.Infrastructure.Accounts;

/// <summary>
/// For runs without Keycloak (tests, AccountDirectory:Mode=Disabled): the workspace is saved but no account is
/// created, so the owner can only use the development persona sign-in.
/// </summary>
internal sealed partial class DisabledAccountDirectory(ILogger<DisabledAccountDirectory> logger) : IAccountDirectory
{
    public Task<Result<string>> CreateWorkspaceOwnerAsync(NewWorkspaceOwner owner, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(owner);
        LogSkipped(logger, owner.TenantId);
        return Task.FromResult(Result.Success($"local-{Guid.NewGuid():N}"));
    }

    public Task DeleteAccountAsync(string accountId, CancellationToken ct) => Task.CompletedTask;

    [LoggerMessage(Level = LogLevel.Warning, Message = "AccountDirectory is disabled: no sign-in account was created for the owner of tenant {TenantId}")]
    private static partial void LogSkipped(ILogger logger, Guid tenantId);
}
