using Recuro.Identity.Domain;
using Recuro.Identity.Domain.Users;

namespace Recuro.Identity.Application.Users;

/// <summary>
/// The frontend's <c>User</c> (frontend/src/domain/types.ts), field for field. <see cref="Id"/> is the
/// Keycloak subject, the id every service records for a person. Title and summary are not in the
/// token yet, so they are empty. <see cref="Department"/> and <see cref="ManagerId"/> are additions for
/// services (HOD and reporting-manager lookups); empty when unknown.
/// </summary>
public sealed record UserDto(
    string Id,
    string TenantId,
    string Name,
    string Initials,
    string Role,
    string Title,
    string Email,
    string Summary,
    string Department,
    string ManagerId)
{
    public static UserDto From(UserAccount user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new UserDto(
            user.Subject,
            user.TenantId.ToString(),
            user.Name,
            InitialsOf(user.Name),
            PersonaRoles.Primary(user.Roles) ?? string.Empty,
            string.Empty,
            user.Email,
            string.Empty,
            user.Department,
            user.ManagerId);
    }

    internal static string InitialsOf(string name) =>
        string.Concat(name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => char.IsLetter(part[0]))
            .Take(2)
            .Select(part => char.ToUpperInvariant(part[0])));
}
