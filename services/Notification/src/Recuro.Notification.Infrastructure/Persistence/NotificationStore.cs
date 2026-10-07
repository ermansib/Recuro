using Microsoft.EntityFrameworkCore;
using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Domain.Directory;
using Recuro.Notification.Domain.Emails;
using Recuro.Notification.Domain.Feed;

namespace Recuro.Notification.Infrastructure.Persistence;

/// <summary>Write side. The tenant query filter on <see cref="NotificationDbContext"/> scopes every lookup.</summary>
internal sealed class NotificationStore(NotificationDbContext db) : INotificationStore
{
    public void Add(FeedItem item) => db.FeedItems.Add(item);

    public void Add(EmailMessage message) => db.Emails.Add(message);

    public void Add(ReadReceipt receipt) => db.ReadReceipts.Add(receipt);

    public void Add(DirectoryUser user) => db.DirectoryUsers.Add(user);

    public void Add(SubjectOwner owner) => db.SubjectOwners.Add(owner);

    public void Add(ContactStatus contact) => db.Contacts.Add(contact);

    public async Task<DirectoryUser?> FindUserAsync(string userId, CancellationToken ct) =>
        db.DirectoryUsers.Local.FirstOrDefault(u => u.UserId == userId)
        ?? await db.DirectoryUsers.FirstOrDefaultAsync(u => u.UserId == userId, ct);

    public async Task<IReadOnlyList<DirectoryUser>> UsersInRoleAsync(string role, CancellationToken ct) =>
        await db.DirectoryUsers.AsNoTracking()
            .Where(u => EF.Property<string[]>(u, "_roles").Contains(role))
            .OrderBy(u => u.UserId)
            .ToListAsync(ct);

    public async Task<SubjectOwner?> FindOwnerAsync(string subject, CancellationToken ct) =>
        db.SubjectOwners.Local.FirstOrDefault(o => o.Subject == subject)
        ?? await db.SubjectOwners.AsNoTracking().FirstOrDefaultAsync(o => o.Subject == subject, ct);

    public async Task<ContactStatus?> FindContactAsync(string address, CancellationToken ct)
    {
        var normalised = ContactStatus.Normalise(address);
        return db.Contacts.Local.FirstOrDefault(c => c.Address == normalised)
            ?? await db.Contacts.FirstOrDefaultAsync(c => c.Address == normalised, ct);
    }

    public Task<EmailMessage?> FindEmailAsync(Guid id, CancellationToken ct) =>
        db.Emails.FirstOrDefaultAsync(m => m.Id == id, ct);

    public async Task<IReadOnlySet<Guid>> ReadItemIdsAsync(string userId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct)
    {
        var ids = itemIds.ToArray();
        var read = await db.ReadReceipts.AsNoTracking()
            .Where(r => r.UserId == userId && ids.Contains(r.ItemId))
            .Select(r => r.ItemId)
            .ToListAsync(ct);
        return read.ToHashSet();
    }
}
