using Recuro.Notification.Application.Abstractions;
using Recuro.Notification.Domain.Directory;
using Recuro.Notification.Domain.Emails;
using Recuro.Notification.Domain.Feed;

namespace Recuro.Notification.UnitTests;

/// <summary>In-memory <see cref="INotificationStore"/> for resolver tests.</summary>
internal sealed class FakeStore : INotificationStore
{
    private readonly List<DirectoryUser> _users = [];
    private readonly List<SubjectOwner> _owners = [];

    public void AddUser(string userId, string name, string email, params string[] roles)
    {
        var user = DirectoryUser.Create(userId, DateTimeOffset.UtcNow);
        user.Update(name, email, roles, DateTimeOffset.UtcNow);
        _users.Add(user);
    }

    public void Add(FeedItem item)
    {
    }

    public void Add(EmailMessage message)
    {
    }

    public void Add(ReadReceipt receipt)
    {
    }

    public void Add(DirectoryUser user) => _users.Add(user);

    public void Add(SubjectOwner owner) => _owners.Add(owner);

    public void Add(ContactStatus contact)
    {
    }

    public Task<DirectoryUser?> FindUserAsync(string userId, CancellationToken ct) => Task.FromResult(_users.FirstOrDefault(u => u.UserId == userId));

    public Task<IReadOnlyList<DirectoryUser>> UsersInRoleAsync(string role, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<DirectoryUser>>(_users.Where(u => u.Roles.Contains(role)).ToList());

    public Task<SubjectOwner?> FindOwnerAsync(string subject, CancellationToken ct) => Task.FromResult(_owners.FirstOrDefault(o => o.Subject == subject));

    public Task<ContactStatus?> FindContactAsync(string address, CancellationToken ct) => Task.FromResult<ContactStatus?>(null);

    public Task<EmailMessage?> FindEmailAsync(Guid id, CancellationToken ct) => Task.FromResult<EmailMessage?>(null);

    public Task<IReadOnlySet<Guid>> ReadItemIdsAsync(string userId, IReadOnlyCollection<Guid> itemIds, CancellationToken ct) =>
        Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
}

/// <summary>Candidate contacts that return a fixed answer.</summary>
internal sealed class FakeContacts(CandidateContact? contact) : ICandidateContacts
{
    public static FakeContacts None { get; } = new(null);

    public Task<CandidateContact?> FindAsync(string candidateId, CancellationToken ct) => Task.FromResult(contact);
}
