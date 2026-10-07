using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recuro.Workflow.Domain.Workflows;

namespace Recuro.Workflow.Infrastructure.Persistence;

internal static class Lengths
{
    public const int Key = 50;
    public const int Id = 100;
    public const int Correlation = 200;
    public const int Text = 2000;
}

internal sealed class WorkflowInstanceConfiguration : IEntityTypeConfiguration<WorkflowInstance>
{
    public void Configure(EntityTypeBuilder<WorkflowInstance> builder)
    {
        builder.ToTable("workflow_instances");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Type).HasMaxLength(Lengths.Key);
        builder.Property(i => i.SubjectType).HasMaxLength(Lengths.Key);
        builder.Property(i => i.SubjectId).HasMaxLength(Lengths.Id);
        builder.Property(i => i.CorrelationKey).HasMaxLength(Lengths.Correlation);
        builder.Property(i => i.ConfigVersionId).HasMaxLength(Lengths.Id);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(i => i.InitiatorId).HasMaxLength(Lengths.Correlation);
        builder.Property(i => i.InitiatorName).HasMaxLength(Lengths.Correlation);
        builder.Property(i => i.CancelReason).HasMaxLength(Lengths.Text);
        builder.Property(i => i.Version).IsRowVersion();
        builder.Ignore(i => i.NextLeg);

        builder.Ignore(i => i.Legs);
        builder.Property<List<LegDefinition>>("_legs")
            .HasColumnName("legs")
            .HasColumnType("jsonb")
            .HasConversion(JsonColumn.Converter<List<LegDefinition>>(), JsonColumn.Comparer<List<LegDefinition>>());
        builder.Property(i => i.Presentation)
            .HasColumnType("jsonb")
            .HasConversion(JsonColumn.Converter<Presentation>(), JsonColumn.Comparer<Presentation>());

        builder.HasMany(i => i.Tasks).WithOne().HasForeignKey(t => t.InstanceId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.Tasks).HasField("_tasks").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        // One live instance per idempotency key; a cancelled one can be started again.
        builder.HasIndex(i => new { i.TenantId, i.CorrelationKey }).IsUnique().HasFilter("status <> 'Cancelled'");
        builder.HasIndex(i => new { i.TenantId, i.SubjectType, i.SubjectId });
    }
}

internal sealed class ApprovalTaskConfiguration : IEntityTypeConfiguration<ApprovalTask>
{
    public void Configure(EntityTypeBuilder<ApprovalTask> builder)
    {
        builder.ToTable("approval_tasks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.Property(t => t.AssigneeRole).HasMaxLength(Lengths.Key);
        builder.Property(t => t.AssigneeLabel).HasMaxLength(Lengths.Correlation);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(32);
        builder.Ignore(t => t.IsOpen);

        builder.Ignore(t => t.Escalations);
        builder.Property<List<EscalationStep>>("_escalations")
            .HasColumnName("escalations")
            .HasColumnType("jsonb")
            .HasConversion(JsonColumn.Converter<List<EscalationStep>>(), JsonColumn.Comparer<List<EscalationStep>>());
        builder.Ignore(t => t.Reminders);
        builder.Property<List<ReminderStep>>("_reminders")
            .HasColumnName("reminders")
            .HasColumnType("jsonb")
            .HasConversion(JsonColumn.Converter<List<ReminderStep>>(), JsonColumn.Comparer<List<ReminderStep>>());
        builder.Property(t => t.Decision)
            .HasColumnType("jsonb")
            .HasConversion(JsonColumn.Converter<TaskDecision>()!, JsonColumn.Comparer<TaskDecision>()!);

        builder.HasIndex(t => new { t.TenantId, t.AssigneeRole, t.Status });

        // The escalation scheduler scans across tenants for due steps (RCU-WFL-006).
        builder.HasIndex(t => t.NextEscalationAt).HasFilter("next_escalation_at IS NOT NULL");
        builder.HasIndex(t => t.NextReminderAt).HasFilter("next_reminder_at IS NOT NULL");
    }
}
