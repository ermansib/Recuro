using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recuro.Notification.Domain.Directory;
using Recuro.Notification.Domain.Emails;
using Recuro.Notification.Domain.Feed;

namespace Recuro.Notification.Infrastructure.Persistence.Configurations;

internal static class Lengths
{
    public const int UserId = 128;
    public const int Role = 32;
    public const int Key = 160;
    public const int Template = 100;
    public const int Text = 500;
    public const int Address = 320;
}

internal sealed class FeedItemConfiguration : IEntityTypeConfiguration<FeedItem>
{
    public void Configure(EntityTypeBuilder<FeedItem> builder)
    {
        builder.ToTable("feed_items");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Sequence).UseIdentityAlwaysColumn();
        builder.HasIndex(i => i.Sequence).IsUnique();
        builder.Property(i => i.RecipientRole).HasMaxLength(Lengths.Role);
        builder.Property(i => i.RecipientUserId).HasMaxLength(Lengths.UserId);
        builder.Property(i => i.RecipientKey).HasMaxLength(Lengths.Key);
        builder.Property(i => i.Icon).HasMaxLength(16);
        builder.Property(i => i.Title).HasMaxLength(Lengths.Text);
        builder.Property(i => i.Body).HasMaxLength(Lengths.Text * 2);
        builder.Property(i => i.Link).HasMaxLength(Lengths.Text);
        builder.Property(i => i.TemplateKey).HasMaxLength(Lengths.Template);
        builder.Property(i => i.MatrixVersion).HasMaxLength(64);

        // Idempotent fan-out (RCU-NTF-001): one item per event, template and recipient.
        builder.HasIndex(i => new { i.TenantId, i.SourceEventId, i.TemplateKey, i.RecipientKey }).IsUnique();
        builder.HasIndex(i => new { i.TenantId, i.RecipientUserId, i.Sequence });
        builder.HasIndex(i => new { i.TenantId, i.RecipientRole, i.Sequence });
    }
}

internal sealed class EmailMessageConfiguration : IEntityTypeConfiguration<EmailMessage>
{
    public void Configure(EntityTypeBuilder<EmailMessage> builder)
    {
        builder.ToTable("email_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.RecipientRole).HasMaxLength(Lengths.Role);
        builder.Property(m => m.RecipientUserId).HasMaxLength(Lengths.UserId);
        builder.Property(m => m.RecipientKey).HasMaxLength(Lengths.Key);
        builder.Property(m => m.ToAddress).HasMaxLength(Lengths.Address);
        builder.Property(m => m.ToName).HasMaxLength(Lengths.Text);
        builder.Property(m => m.FromAddress).HasMaxLength(Lengths.Address);
        builder.Property(m => m.FromName).HasMaxLength(Lengths.Text);
        builder.Property(m => m.Tag).HasMaxLength(64);
        builder.Property(m => m.Subject).HasMaxLength(Lengths.Text);
        builder.Ignore(m => m.Paragraphs);
        builder.Property<string[]>("_paragraphs").HasColumnName("paragraphs");
        builder.Property(m => m.Cta).HasMaxLength(Lengths.Text);
        builder.Property(m => m.Link).HasMaxLength(Lengths.Text);
        builder.Property(m => m.Signature).HasMaxLength(Lengths.Text);
        builder.Property(m => m.TemplateKey).HasMaxLength(Lengths.Template);
        builder.Property(m => m.TemplateVersion).HasMaxLength(64);
        builder.Property(m => m.MatrixVersion).HasMaxLength(64);
        builder.Property(m => m.SourceEventType).HasMaxLength(Lengths.Template);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(m => m.ProviderMessageId).HasMaxLength(Lengths.Text);
        builder.Property(m => m.LastError).HasMaxLength(Lengths.Text);

        builder.HasIndex(m => new { m.TenantId, m.SourceEventId, m.TemplateKey, m.RecipientKey }).IsUnique();
        builder.HasIndex(m => new { m.Status, m.NextAttemptAt });
        builder.HasIndex(m => new { m.TenantId, m.RecipientUserId, m.CreatedAt });
        builder.HasIndex(m => new { m.TenantId, m.CreatedAt });
    }
}

internal sealed class ReadReceiptConfiguration : IEntityTypeConfiguration<ReadReceipt>
{
    public void Configure(EntityTypeBuilder<ReadReceipt> builder)
    {
        builder.ToTable("read_receipts");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.UserId).HasMaxLength(Lengths.UserId);
        builder.HasIndex(r => new { r.TenantId, r.UserId, r.ItemId }).IsUnique();
    }
}

internal sealed class DirectoryUserConfiguration : IEntityTypeConfiguration<DirectoryUser>
{
    public void Configure(EntityTypeBuilder<DirectoryUser> builder)
    {
        builder.ToTable("directory_users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.UserId).HasMaxLength(Lengths.UserId);
        builder.Property(u => u.Name).HasMaxLength(Lengths.Text);
        builder.Property(u => u.Email).HasMaxLength(Lengths.Address);
        builder.Ignore(u => u.Roles);
        builder.Property<string[]>("_roles").HasColumnName("roles");
        builder.HasIndex(u => new { u.TenantId, u.UserId }).IsUnique();
    }
}

internal sealed class SubjectOwnerConfiguration : IEntityTypeConfiguration<SubjectOwner>
{
    public void Configure(EntityTypeBuilder<SubjectOwner> builder)
    {
        builder.ToTable("subject_owners");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Subject).HasMaxLength(Lengths.Text);
        builder.Property(o => o.UserId).HasMaxLength(Lengths.UserId);
        builder.Property(o => o.Name).HasMaxLength(Lengths.Text);
        builder.HasIndex(o => new { o.TenantId, o.Subject }).IsUnique();
    }
}

internal sealed class ContactStatusConfiguration : IEntityTypeConfiguration<ContactStatus>
{
    public void Configure(EntityTypeBuilder<ContactStatus> builder)
    {
        builder.ToTable("contact_statuses");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Address).HasMaxLength(Lengths.Address);
        builder.Property(c => c.Reason).HasMaxLength(Lengths.Text);
        builder.HasIndex(c => new { c.TenantId, c.Address }).IsUnique();
    }
}
