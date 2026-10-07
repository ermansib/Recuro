using Recuro.BuildingBlocks.Domain;
using Recuro.Notification.Domain.Matrix;

namespace Recuro.Notification.Domain.Feed;

/// <summary>
/// A resolved recipient: one user (with a display name and an email address when the directory knows
/// them) or, for HR staff roles, everyone in a role.
/// </summary>
public sealed record Recipient(string Role, string? UserId, string? Name, string? Email)
{
    public static Recipient Everyone(string role) => new(role, null, null, null);

    public bool IsRoleWide => UserId is null;

    public string Key => UserId ?? $"role:{Role}";

    /// <summary>Candidates and employees only ever receive their own items, never a role-wide broadcast.</summary>
    public Result EnsureAddressable()
    {
        if (!NotificationMatrix.KnownRoles.Contains(Role))
        {
            return Error.Validation("unknown_role", $"Unknown recipient role '{Role}'.");
        }

        return IsRoleWide && !NotificationMatrix.BroadcastRoles.Contains(Role)
            ? Error.Validation("role_wide_not_allowed", $"Notifications for '{Role}' must name a user.")
            : Result.Success();
    }
}
