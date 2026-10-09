using FluentValidation;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Identity.Application.Abstractions;
using Recuro.Identity.Domain;
using Recuro.Identity.Domain.Users;

namespace Recuro.Identity.Application.Users.Queries.ListUsers;

/// <summary>
/// The tenant's people, by name. Without filters it is the frontend's <c>listTeam()</c> (staff only, no
/// candidates); with a role it is how Notification and Workflow find recipients for a role, and with
/// <c>role=hod&amp;department=…</c> how Onboarding finds a department's head.
/// </summary>
public sealed record ListUsersQuery(string? Role, string? Department = null) : IQuery<IReadOnlyList<UserDto>>;

internal sealed class ListUsersQueryValidator : AbstractValidator<ListUsersQuery>
{
    public ListUsersQueryValidator()
    {
        RuleFor(q => q.Role!)
            .Must(PersonaRoles.IsMirrored)
            .When(q => q.Role is not null)
            .WithMessage("role must be one of: " + string.Join(", ", PersonaRoles.All) + ", " + PersonaRoles.TenantAdmin + ", " + PersonaRoles.Hod + ".");
        RuleFor(q => q.Department!)
            .Must(UserPlacement.IsDepartmentKey)
            .When(q => q.Department is not null)
            .WithMessage("department is a lowercase key of letters, digits and dashes, e.g. operations.");
    }
}

internal sealed class ListUsersQueryHandler(IUserAccounts users) : IQueryHandler<ListUsersQuery, IReadOnlyList<UserDto>>
{
    public async Task<Result<IReadOnlyList<UserDto>>> Handle(ListUsersQuery query, CancellationToken ct)
    {
        var people = await users.ListAsync(query.Role, query.Department, ct);
        IReadOnlyList<UserDto> result = people
            .Where(u => query.Role is not null || PersonaRoles.Primary(u.Roles) != PersonaRoles.Candidate)
            .Select(UserDto.From)
            .ToList();
        return Result.Success(result);
    }
}
