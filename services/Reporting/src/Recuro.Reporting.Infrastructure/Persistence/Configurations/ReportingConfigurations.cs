using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Recuro.Reporting.Application.Reports;
using Recuro.Reporting.Domain.Events;
using Recuro.Reporting.Domain.Kpis;
using Recuro.Reporting.Domain.Projections;
using Recuro.Reporting.Domain.Reports;

namespace Recuro.Reporting.Infrastructure.Persistence.Configurations;

internal sealed class ReportEventConfiguration : IEntityTypeConfiguration<ReportEvent>
{
    public void Configure(EntityTypeBuilder<ReportEvent> builder)
    {
        builder.ToTable("report_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Sequence).UseIdentityAlwaysColumn();
        builder.Property(e => e.Type).HasMaxLength(ReportingLimits.TypeLength);
        builder.Property(e => e.Subject).HasMaxLength(ReportingLimits.SubjectLength);
        builder.Property(e => e.Data).HasColumnType("jsonb");
        builder.HasIndex(e => e.Sequence).IsUnique();
        builder.HasIndex(e => new { e.TenantId, e.Sequence });
    }
}

internal sealed class ProjectionCheckpointConfiguration : IEntityTypeConfiguration<ProjectionCheckpoint>
{
    public void Configure(EntityTypeBuilder<ProjectionCheckpoint> builder)
    {
        builder.ToTable("projection_checkpoints");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Projection).HasMaxLength(ReportingLimits.IdLength);
        builder.HasIndex(c => new { c.TenantId, c.Projection }).IsUnique();
    }
}

internal sealed class ApplicationFactConfiguration : IEntityTypeConfiguration<ApplicationFact>
{
    public void Configure(EntityTypeBuilder<ApplicationFact> builder)
    {
        builder.ToTable("application_facts");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.AppId).HasMaxLength(ReportingLimits.IdLength);
        builder.Property(a => a.ReqId).HasMaxLength(ReportingLimits.IdLength);
        builder.Property(a => a.CandidateId).HasMaxLength(ReportingLimits.IdLength);
        builder.Property(a => a.Source).HasMaxLength(ReportingLimits.SourceLength);
        builder.Property(a => a.FurthestStage).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(a => a.IsHire);
        builder.HasIndex(a => new { a.TenantId, a.AppId }).IsUnique();
        builder.HasIndex(a => new { a.TenantId, a.CreatedAt });
        builder.HasIndex(a => new { a.TenantId, a.OfferAcceptedAt });
        builder.HasIndex(a => new { a.TenantId, a.CandidateId });
    }
}

internal sealed class RequisitionFactConfiguration : IEntityTypeConfiguration<RequisitionFact>
{
    public void Configure(EntityTypeBuilder<RequisitionFact> builder)
    {
        builder.ToTable("requisition_facts");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ReqId).HasMaxLength(ReportingLimits.IdLength);
        builder.Property(r => r.Grade).HasMaxLength(ReportingLimits.IdLength);
        builder.Property(r => r.BudgetStatus).HasMaxLength(10);
        builder.HasIndex(r => new { r.TenantId, r.ReqId }).IsUnique();
        builder.HasIndex(r => new { r.TenantId, r.ApprovedAt });
    }
}

internal sealed class FeedbackFactConfiguration : IEntityTypeConfiguration<FeedbackFact>
{
    public void Configure(EntityTypeBuilder<FeedbackFact> builder)
    {
        builder.ToTable("feedback_facts");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.InterviewId).HasMaxLength(ReportingLimits.IdLength);
        builder.Property(f => f.InterviewerId).HasMaxLength(ReportingLimits.ActorLength);
        builder.Property(f => f.AppId).HasMaxLength(ReportingLimits.IdLength);
        builder.Ignore(f => f.CountsAt);
        builder.Ignore(f => f.OnTime);
        builder.HasIndex(f => new { f.TenantId, f.InterviewId, f.InterviewerId }).IsUnique();
        builder.HasIndex(f => new { f.TenantId, f.SubmittedAt });
        builder.HasIndex(f => new { f.TenantId, f.OverdueAt });
    }
}

internal sealed class RecruitmentCostConfiguration : IEntityTypeConfiguration<RecruitmentCost>
{
    public void Configure(EntityTypeBuilder<RecruitmentCost> builder)
    {
        builder.ToTable("recruitment_costs");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Source).HasMaxLength(ReportingLimits.SourceLength);
        builder.Property(c => c.Amount).HasPrecision(18, 2);
        builder.Property(c => c.Currency).HasMaxLength(ReportingLimits.CurrencyLength).IsFixedLength();
        builder.Property(c => c.Note).HasMaxLength(ReportingLimits.NoteLength);
        builder.Property(c => c.RecordedBy).HasMaxLength(ReportingLimits.ActorLength);
        builder.HasIndex(c => new { c.TenantId, c.Month });
    }
}

internal sealed class MetricDefinitionSetConfiguration : IEntityTypeConfiguration<MetricDefinitionSet>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public void Configure(EntityTypeBuilder<MetricDefinitionSet> builder)
    {
        builder.ToTable("metric_definition_sets");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.CreatedBy).HasMaxLength(ReportingLimits.ActorLength);
        builder.Property(s => s.Definitions)
            .HasColumnType("jsonb")
            .HasConversion(
                new ValueConverter<IReadOnlyList<MetricDefinition>, string>(
                    list => JsonSerializer.Serialize(list, Json),
                    json => JsonSerializer.Deserialize<List<MetricDefinition>>(json, Json) ?? new List<MetricDefinition>()),
                new ValueComparer<IReadOnlyList<MetricDefinition>>(
                    (a, b) => a != null && b != null && a.SequenceEqual(b),
                    list => list.Aggregate(0, (hash, item) => HashCode.Combine(hash, item)),
                    list => list.ToList()));
        builder.HasIndex(s => new { s.TenantId, s.Version }).IsUnique();
    }
}

internal sealed class ReportSettingsConfiguration : IEntityTypeConfiguration<ReportSettings>
{
    public void Configure(EntityTypeBuilder<ReportSettings> builder)
    {
        builder.ToTable("report_settings");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TimeZone).HasMaxLength(ReportingLimits.TimeZoneLength);
        builder.Property(s => s.MonthlyRecipientRole).HasMaxLength(ReportingLimits.RoleLength);
        builder.Property(s => s.QuarterlyRecipientRole).HasMaxLength(ReportingLimits.RoleLength);
        builder.Property(s => s.UpdatedBy).HasMaxLength(ReportingLimits.ActorLength);
        builder.HasIndex(s => s.TenantId).IsUnique();
    }
}

internal sealed class KpiSnapshotConfiguration : IEntityTypeConfiguration<KpiSnapshot>
{
    public void Configure(EntityTypeBuilder<KpiSnapshot> builder)
    {
        builder.ToTable("kpi_snapshots");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Period).HasMaxLength(10);
        builder.Property(s => s.Cadence).HasConversion<string>().HasMaxLength(10);
        builder.Property(s => s.Payload).HasColumnType("jsonb");
        builder.Property(s => s.Hash).HasMaxLength(ReportingLimits.HashLength).IsFixedLength();
        builder.Property(s => s.Reason).HasMaxLength(ReportingLimits.ReasonLength);
        builder.Property(s => s.ComputedBy).HasMaxLength(ReportingLimits.ActorLength);
        builder.HasIndex(s => new { s.TenantId, s.Period, s.Revision }).IsUnique();
    }
}

internal sealed class ReportPackConfiguration : IEntityTypeConfiguration<ReportPack>
{
    public void Configure(EntityTypeBuilder<ReportPack> builder)
    {
        builder.ToTable("report_packs");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Period).HasMaxLength(10);
        builder.Property(p => p.Cadence).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.RecipientRole).HasMaxLength(ReportingLimits.RoleLength);
        builder.Property(p => p.SnapshotHash).HasMaxLength(ReportingLimits.HashLength).IsFixedLength();
        builder.Property(p => p.Delivery).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.CreatedBy).HasMaxLength(ReportingLimits.ActorLength);
        builder.HasIndex(p => new { p.TenantId, p.Period, p.Cadence, p.RecipientRole });
        builder.HasIndex(p => p.ReadyEventId);
    }
}
