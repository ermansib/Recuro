using FluentValidation;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Identity.Application.Abstractions;
using Recuro.Identity.Domain;

namespace Recuro.Identity.Application.Users.Queries.ListUsers;

/// <summary>
/// The tenant's people, by name. Without a role it is the frontend's <c>listTeam()</c> (staff only, no
/// candidates); with one it is how Notification and Workflow find recipients for a role.
/// </summary>
public sealed record ListUsersQuery(string? Role) : IQuery<IReadOnlyList<UserDto>>;

internal sealed class ListUsersQueryValidator : AbstractValidator<ListUsersQuery>
{
    public ListUsersQueryValidator() =>
        RuleFor(q => q.Role!)
            .Must(PersonaRoles.IsMirrored)
            .When(q => q.Role is not null)
            .WithMessage("role must be one of: " + string.Join(", ", PersonaRoles.All) + ", " + PersonaRoles.TenantAdmin + ".");
}

internal sealed class ListUsersQueryHandler(IUserAccounts users) : IQueryHandler<ListUsersQuery, IReadOnlyList<UserDto>>
{
    public async Task<Result<IReadOnlyList<UserDto>>> Handle(ListUsersQuery query, CancellationToken ct)
    {
        var people = await users.ListAsync(query.Role, ct);
        IReadOnlyList<UserDto> result = people
            .Where(u => query.Role is not null || PersonaRoles.Primary(u.Roles) != PersonaRoles.Candidate)
            .Select(UserDto.From)
            .ToList();
        return Result.Success(result);
    }
}
