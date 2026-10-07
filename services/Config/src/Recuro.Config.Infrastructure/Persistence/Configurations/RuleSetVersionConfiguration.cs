using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recuro.Config.Application;
using Recuro.Config.Domain.RuleSets;

namespace Recuro.Config.Infrastructure.Persistence.Configurations;

internal sealed class RuleSetVersionConfiguration : IEntityTypeConfiguration<RuleSetVersion>
{
    public void Configure(EntityTypeBuilder<RuleSetVersion> builder)
    {
        builder.ToTable("rule_set_versions");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.MatrixType).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.Content).HasColumnType("jsonb");
        builder.Property(v => v.Note).HasMaxLength(ConfigLimits.NoteLength);
        builder.Property(v => v.ProposedById).HasMaxLength(ConfigLimits.ActorLength);
        builder.Property(v => v.ProposedByName).HasMaxLength(ConfigLimits.ActorLength);
        builder.Property(v => v.DecidedById).HasMaxLength(ConfigLimits.ActorLength);
        builder.Property(v => v.DecidedByName).HasMaxLength(ConfigLimits.ActorLength);
        builder.Property(v => v.RejectionReason).HasMaxLength(ConfigLimits.ReasonLength);

        // Optimistic concurrency on PostgreSQL's row version: two approvers can't both win.
        builder.Property<uint>("RowVersion").IsRowVersion();

        builder.HasIndex(v => new { v.TenantId, v.MatrixType, v.Number }).IsUnique();
        builder.HasIndex(v => new { v.TenantId, v.MatrixType, v.Status, v.EffectiveFrom });
    }
}
