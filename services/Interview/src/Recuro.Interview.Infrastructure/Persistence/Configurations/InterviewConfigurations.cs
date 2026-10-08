using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recuro.Interview.Domain.Applications;
using Recuro.Interview.Domain.Interviews;
using Recuro.Interview.Domain.Selection;

namespace Recuro.Interview.Infrastructure.Persistence.Configurations;

internal sealed class InterviewRoundConfiguration : IEntityTypeConfiguration<InterviewRound>
{
    public void Configure(EntityTypeBuilder<InterviewRound> builder)
    {
        builder.ToTable("interviews");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.AppId).HasMaxLength(InterviewLimits.IdLength);
        builder.Property(i => i.ReqId).HasMaxLength(InterviewLimits.IdLength);
        builder.Property(i => i.CandidateId).HasMaxLength(InterviewLimits.IdLength);
        builder.Property(i => i.Grade).HasMaxLength(InterviewLimits.IdLength);
        builder.Property(i => i.RoundType).HasMaxLength(InterviewLimits.IdLength);
        builder.Property(i => i.RoundLabel).HasMaxLength(InterviewLimits.ShortText);
        builder.Property(i => i.Mode).HasConversion<string>().HasMaxLength(32);
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(i => i.ConfigVersionId).HasMaxLength(100);
        builder.Property(i => i.ScheduledBy).HasMaxLength(InterviewLimits.ShortText);
        builder.Property(i => i.CancelReason).HasMaxLength(InterviewLimits.LongText);
        builder.Property(i => i.Version).IsRowVersion();
        builder.Ignore(i => i.Title);
        builder.Ignore(i => i.PendingInterviewerIds);

        builder.Property<List<string>>("_competencies")
            .HasColumnName("competencies")
            .HasColumnType("jsonb")
            .HasConversion(JsonColumn.ListConverter<string>(), JsonColumn.ListComparer<string>());
        builder.Property<List<string>>("_attachments")
            .HasColumnName("attachments")
            .HasColumnType("jsonb")
            .HasConversion(JsonColumn.ListConverter<string>(), JsonColumn.ListComparer<string>());
        builder.Ignore(i => i.Competencies);
        builder.Ignore(i => i.Attachments);

        builder.HasMany(i => i.Assessments).WithOne().HasForeignKey(a => a.InterviewId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.Assessments).HasField("_assessments").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

        builder.HasIndex(i => new { i.TenantId, i.AppId });

        // The SLA scanner's query: open rounds by threshold.
        builder.HasIndex(i => new { i.Status, i.ReminderAt });
    }
}

internal sealed class AssessmentConfiguration : IEntityTypeConfiguration<Assessment>
{
    public void Configure(EntityTypeBuilder<Assessment> builder)
    {
        builder.ToTable("assessments");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.InterviewerId).HasMaxLength(InterviewLimits.ShortText);
        builder.Property(a => a.InterviewerName).HasMaxLength(InterviewLimits.ShortText);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Recommendation).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Justification).HasMaxLength(InterviewLimits.LongText);
        builder.Property(a => a.SubmittedBy).HasMaxLength(InterviewLimits.ShortText);
        builder.Property(a => a.Average).HasPrecision(3, 1);
        builder.Property(a => a.Version).IsRowVersion();
        builder.Ignore(a => a.IsSubmitted);

        builder.Property<List<Rating>>("_ratings")
            .HasColumnName("ratings")
            .HasColumnType("jsonb")
            .HasConversion(JsonColumn.ListConverter<Rating>(), JsonColumn.ListComparer<Rating>());
        builder.Property<List<string>>("_flags")
            .HasColumnName("flags")
            .HasColumnType("jsonb")
            .HasConversion(JsonColumn.ListConverter<string>(), JsonColumn.ListComparer<string>());
        builder.Property<List<AssessmentRevision>>("_history")
            .HasColumnName("history")
            .HasColumnType("jsonb")
            .HasConversion(JsonColumn.ListConverter<AssessmentRevision>(), JsonColumn.ListComparer<AssessmentRevision>());
        builder.Ignore(a => a.Ratings);
        builder.Ignore(a => a.Flags);
        builder.Ignore(a => a.History);

        builder.HasIndex(a => new { a.TenantId, a.InterviewerId });
    }
}

internal sealed class ApplicationTrackConfiguration : IEntityTypeConfiguration<ApplicationTrack>
{
    public void Configure(EntityTypeBuilder<ApplicationTrack> builder)
    {
        builder.ToTable("application_tracks");
        builder.Property<Guid>("Id").ValueGeneratedOnAdd();
        builder.HasKey("Id");
        builder.HasIndex(t => new { t.TenantId, t.AppId }).IsUnique();
        builder.Property(t => t.AppId).HasMaxLength(InterviewLimits.IdLength);
        builder.Property(t => t.ReqId).HasMaxLength(InterviewLimits.IdLength);
        builder.Property(t => t.CandidateId).HasMaxLength(InterviewLimits.IdLength);
        builder.Property(t => t.Stage).HasMaxLength(32);
        builder.Ignore(t => t.InInterview);
    }
}

internal sealed class SelectionDecisionConfiguration : IEntityTypeConfiguration<SelectionDecision>
{
    public void Configure(EntityTypeBuilder<SelectionDecision> builder)
    {
        builder.ToTable("selections");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.AppId).HasMaxLength(InterviewLimits.IdLength);
        builder.Property(s => s.ReqId).HasMaxLength(InterviewLimits.IdLength);
        builder.Property(s => s.Grade).HasMaxLength(InterviewLimits.IdLength);
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(s => s.OverallAverage).HasPrecision(3, 1);
        builder.Property(s => s.ConfigVersionId).HasMaxLength(100);
        builder.Property(s => s.SubmittedBy).HasMaxLength(InterviewLimits.ShortText);
        builder.Property(s => s.DecidedBy).HasMaxLength(InterviewLimits.ShortText);
        builder.Property(s => s.Reason).HasMaxLength(InterviewLimits.LongText);
        builder.Property(s => s.Version).IsRowVersion();
        builder.HasIndex(s => new { s.TenantId, s.AppId }).IsUnique();
        builder.HasIndex(s => s.WorkflowInstanceId);
    }
}
