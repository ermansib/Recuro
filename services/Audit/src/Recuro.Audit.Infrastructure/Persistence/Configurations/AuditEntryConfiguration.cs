using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recuro.Audit.Application.Entries;
using Recuro.Audit.Domain.Entries;

namespace Recuro.Audit.Infrastructure.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("audit_entries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.ActorId).HasMaxLength(AuditLimits.ActorLength);
        builder.Property(e => e.ActorName).HasMaxLength(AuditLimits.ActorLength);
        builder.Property(e => e.ActorRole).HasMaxLength(AuditLimits.RoleLength);
        builder.Property(e => e.Entity).HasMaxLength(AuditLimits.EntityLength);
        builder.Property(e => e.Action).HasMaxLength(AuditLimits.ActionLength);
        builder.Property(e => e.Before).HasMaxLength(AuditLimits.StateLength);
        builder.Property(e => e.After).HasMaxLength(AuditLimits.StateLength);
        builder.Property(e => e.Reason).HasMaxLength(AuditLimits.ReasonLength);
        builder.Property(e => e.ConfigVersion).HasMaxLength(AuditLimits.ConfigVersionLength);
        builder.Property(e => e.IpAddress).HasMaxLength(64);
        builder.Property(e => e.CorrelationId).HasMaxLength(128);
        builder.Property(e => e.Source).HasMaxLength(AuditLimits.SourceLength);
        builder.Property(e => e.PreviousHash).HasMaxLength(64).IsFixedLength();
        builder.Property(e => e.Hash).HasMaxLength(64).IsFixedLength();

        // One chain per tenant with no forks; the index also serves chain reads in order.
        builder.HasIndex(e => new { e.TenantId, e.Sequence }).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.OccurredAt });
        builder.HasIndex(e => new { e.TenantId, e.Entity });
        builder.HasIndex(e => new { e.TenantId, e.CorrelationId });
    }
}

internal sealed class AuditSealConfiguration : IEntityTypeConfiguration<AuditSeal>
{
    public void Configure(EntityTypeBuilder<AuditSeal> builder)
    {
        builder.ToTable("audit_seals");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Hash).HasMaxLength(64).IsFixedLength();
        builder.HasIndex(s => new { s.TenantId, s.UpToSequence }).IsUnique();
    }
}
