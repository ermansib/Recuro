using Microsoft.EntityFrameworkCore;
using Npgsql;
using Recuro.Identity.Application.Abstractions;
using Recuro.Identity.Domain.Users;
using Recuro.Identity.Infrastructure.Persistence.Configurations;

namespace Recuro.Identity.Infrastructure.Persistence;

/// <summary>The tenant query filter on <see cref="IdentityDbContext"/> scopes every query.</summary>
internal sealed class UserAccounts(IdentityDbContext db) : IUserAccounts
{
    public Task<UserAccount?> FindBySubjectAsync(string subject, CancellationToken ct) =>
        db.Users.FirstOrDefaultAsync(u => u.Subject == subject, ct);

    public async Task<IReadOnlyList<UserAccount>> ListAsync(string? role, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking();
        if (role is not null)
        {
            query = query.Where(u => EF.Property<List<string>>(u, UserAccountConfiguration.RolesField).Contains(role));
        }

        return await query.OrderBy(u => u.Name).ToListAsync(ct);
    }

    public async Task<bool> TryAddAsync(UserAccount user, CancellationToken ct)
    {
        db.Users.Add(user);
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Lost the race: drop our row and its outbox event, the winner's are already committed.
            db.ChangeTracker.Clear();
            return false;
        }
    }
}
