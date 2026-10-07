using Microsoft.EntityFrameworkCore;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Persistence;
using Recuro.Notification.Domain.Directory;
using Recuro.Notification.Domain.Emails;
using Recuro.Notification.Domain.Feed;

namespace Recuro.Notification.Infrastructure.Persistence;

/// <summary>The notification service's own database (recuro_notification). No other service reads it.</summary>
public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options, ITenantContext tenant, IDomainEventDispatcher domainEvents)
    : RecuroDbContext(options, tenant, domainEvents)
{
    public DbSet<FeedItem> FeedItems => Set<FeedItem>();

    public DbSet<EmailMessage> Emails => Set<EmailMessage>();

    public DbSet<ReadReceipt> ReadReceipts => Set<ReadReceipt>();

    public DbSet<DirectoryUser> DirectoryUsers => Set<DirectoryUser>();

    public DbSet<SubjectOwner> SubjectOwners => Set<SubjectOwner>();

    public DbSet<ContactStatus> Contacts => Set<ContactStatus>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(NotificationDbContext).Assembly);
}
