using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Identity.Application.Abstractions;

namespace Recuro.Identity.Application.Users.Queries.GetUser;

/// <summary>One person of the tenant by user id, e.g. a joiner's reporting manager (<c>managerId</c>).</summary>
public sealed record GetUserQuery(string Id) : IQuery<UserDto>;

internal sealed class GetUserQueryHandler(IUserAccounts users) : IQueryHandler<GetUserQuery, UserDto>
{
    public async Task<Result<UserDto>> Handle(GetUserQuery query, CancellationToken ct)
    {
        var user = query.Id.Length <= UserLimits.SubjectLength ? await users.FindBySubjectAsync(query.Id, ct) : null;
        return user is null
            ? Error.NotFound("user_not_found", $"No user '{query.Id}' in this tenant.")
            : UserDto.From(user);
    }
}
