using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recuro.Pipeline.Application.Applications;
using Recuro.Pipeline.Domain.Applications;
using Recuro.Pipeline.Domain.Requisitions;
using ApplicationEntity = Recuro.Pipeline.Domain.Applications.Application;

namespace Recuro.Pipeline.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationConfiguration : IEntityTypeConfiguration<ApplicationEntity>
{
    public void Configure(EntityTypeBuilder<ApplicationEntity> builder)
    {
        builder.ToTable("applications");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.AppId).HasMaxLength(PipelineLimits.AppIdLength);
        builder.Property(a => a.ReqId).HasMaxLength(PipelineLimits.ReqIdLength);
        builder.Property(a => a.CandidateId).HasMaxLength(PipelineLimits.CandidateIdLength);
        builder.Property(a => a.Source).HasMaxLength(PipelineLimits.SourceLength);
        builder.Property(a => a.Note).HasMaxLength(PipelineLimits.NoteLength + PipelineLimits.ReasonLength);
        builder.Property(a => a.Stage).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.HeldFromStage).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(a => a.IsClosed);

        // RCU-PPL-002: optimistic concurrency on PostgreSQL's xmin, so a stale move fails instead of overwriting.
        builder.Property<uint>("Version").IsRowVersion();

        builder.OwnsOne(a => a.Rejection, r =>
        {
            r.Property(x => x.Reason).HasColumnName("rejection_reason").HasMaxLength(PipelineLimits.ReasonLength);
            r.Property(x => x.RegretDueBy).HasColumnName("rejection_regret_due_by");
            r.Property(x => x.RetainUntil).HasColumnName("rejection_retain_until");
        });

        builder.OwnsMany(a => a.StageHistory, move =>
        {
            move.ToTable("application_stage_moves");
            move.WithOwner().HasForeignKey("ApplicationId");
            move.Property<int>("Id");
            move.HasKey("Id");
            move.Property(x => x.From).HasConversion<string>().HasMaxLength(20);
            move.Property(x => x.To).HasConversion<string>().HasMaxLength(20);
            move.Property(x => x.By).HasMaxLength(PipelineLimits.ActorLength);
            move.Property(x => x.ById).HasMaxLength(PipelineLimits.ActorLength);
        });
        builder.Navigation(a => a.StageHistory).HasField("_stageHistory").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(a => new { a.TenantId, a.AppId }).IsUnique();
        builder.HasIndex(a => new { a.TenantId, a.ReqId, a.Stage });
        builder.HasIndex(a => new { a.TenantId, a.ReqId, a.CandidateId });
        builder.HasIndex(a => new { a.Stage, a.StageEnteredAt }).HasFilter("tat_breached_at IS NULL");
    }
}

internal sealed class SourcingGateConfiguration : IEntityTypeConfiguration<SourcingGate>
{
    public void Configure(EntityTypeBuilder<SourcingGate> builder)
    {
        builder.ToTable("sourcing_gates");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.ReqId).HasMaxLength(PipelineLimits.ReqIdLength);
        builder.HasIndex(g => new { g.TenantId, g.ReqId }).IsUnique();
    }
}

internal sealed class ApplicationCounterConfiguration : IEntityTypeConfiguration<ApplicationCounter>
{
    public void Configure(EntityTypeBuilder<ApplicationCounter> builder)
    {
        builder.ToTable("application_counters");
        builder.HasKey(c => new { c.TenantId, c.Year });
    }
}
