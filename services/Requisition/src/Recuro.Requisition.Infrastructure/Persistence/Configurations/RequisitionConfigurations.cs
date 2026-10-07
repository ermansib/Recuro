using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recuro.Requisition.Domain.JobDescriptions;
using Recuro.Requisition.Domain.Requisitions;

namespace Recuro.Requisition.Infrastructure.Persistence.Configurations;

internal sealed class ManpowerRequisitionConfiguration : IEntityTypeConfiguration<ManpowerRequisition>
{
    public void Configure(EntityTypeBuilder<ManpowerRequisition> builder)
    {
        builder.ToTable("requisitions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.ReqId).HasMaxLength(RequisitionLimits.ReqIdLength);
        builder.Property(r => r.State).HasConversion<string>().HasMaxLength(32);
        builder.Property(r => r.OwnerId).HasMaxLength(RequisitionLimits.ShortText);
        builder.Property(r => r.OwnerName).HasMaxLength(RequisitionLimits.ShortText);
        builder.Property(r => r.ConfigVersionId).HasMaxLength(RequisitionLimits.ConfigVersionLength);
        builder.Property(r => r.Reason).HasMaxLength(RequisitionLimits.LongText);
        builder.Property(r => r.Version).IsRowVersion();
        builder.Ignore(r => r.HasIssuedReqId);
        builder.Ignore(r => r.SourcingAllowed);

        builder.ComplexProperty(r => r.Details, d =>
        {
            d.Property(x => x.Department).HasColumnName("department").HasMaxLength(RequisitionLimits.ShortText);
            d.Property(x => x.Designation).HasColumnName("designation").HasMaxLength(RequisitionLimits.ShortText);
            d.Property(x => x.Grade).HasColumnName("grade").HasMaxLength(RequisitionLimits.ShortText);
            d.Property(x => x.Location).HasColumnName("location").HasMaxLength(RequisitionLimits.ShortText);
            d.Property(x => x.Positions).HasColumnName("positions");
            d.Property(x => x.ReportingManager).HasColumnName("reporting_manager").HasMaxLength(RequisitionLimits.ShortText);
            d.Property(x => x.EmploymentType).HasColumnName("employment_type").HasConversion<string>().HasMaxLength(32);
            d.Property(x => x.Nature).HasColumnName("nature").HasConversion<string>().HasMaxLength(32);
            d.Property(x => x.ReplacementReason).HasColumnName("replacement_reason").HasMaxLength(RequisitionLimits.LongText);
            d.Property(x => x.JoiningDate).HasColumnName("joining_date");
            d.Property(x => x.Band).HasColumnName("band").HasMaxLength(RequisitionLimits.ShortText);
            d.Property(x => x.OutOfBudget).HasColumnName("out_of_budget");
            d.Property(x => x.OobJustification).HasColumnName("oob_justification").HasMaxLength(RequisitionLimits.LongText);
            d.Property(x => x.Qualifications).HasColumnName("qualifications").HasMaxLength(RequisitionLimits.LongText);
            d.Property(x => x.SourcingChannels).HasColumnName("sourcing_channels").HasColumnType("jsonb")
                .HasConversion(Persistence.JsonColumn.Converter<string>(), Persistence.JsonColumn.Comparer<string>());
        });

        builder.ComplexProperty(r => r.Route, route =>
        {
            route.Property(x => x.Initiating).HasColumnName("route_initiating").HasMaxLength(RequisitionLimits.ShortText);
            route.Property(x => x.Recommending).HasColumnName("route_recommending").HasMaxLength(RequisitionLimits.ShortText);
            route.Property(x => x.Approving).HasColumnName("route_approving").HasMaxLength(RequisitionLimits.ShortText);
            route.Property(x => x.ApproverRole).HasColumnName("route_approver_role").HasMaxLength(50);
        });

        builder.HasIndex(r => new { r.TenantId, r.ReqId }).IsUnique();
        builder.HasIndex(r => new { r.TenantId, r.State });
        builder.HasIndex(r => new { r.TenantId, r.CreatedAt });
    }
}

internal sealed class JobDescriptionConfiguration : IEntityTypeConfiguration<JobDescription>
{
    public void Configure(EntityTypeBuilder<JobDescription> builder)
    {
        const int text = 2000;
        builder.ToTable("job_descriptions");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.ReqId).HasMaxLength(RequisitionLimits.ReqIdLength);
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(j => j.Version).IsRowVersion();
        builder.Ignore(j => j.History);
        builder.Property<List<JobDescriptionRevision>>("_history")
            .HasColumnName("history")
            .HasColumnType("jsonb")
            .HasConversion(Persistence.JsonColumn.ListConverter<JobDescriptionRevision>(), Persistence.JsonColumn.ListComparer<JobDescriptionRevision>());

        builder.ComplexProperty(j => j.Content, c =>
        {
            c.Property(x => x.Purpose).HasColumnName("purpose").HasMaxLength(text);
            c.Property(x => x.ReportsTo).HasColumnName("reports_to").HasMaxLength(text);
            c.Property(x => x.TeamSize).HasColumnName("team_size").HasMaxLength(text);
            c.Property(x => x.Location).HasColumnName("location").HasMaxLength(text);
            c.Property(x => x.MinQualification).HasColumnName("min_qualification").HasMaxLength(text);
            c.Property(x => x.Experience).HasColumnName("experience").HasMaxLength(text);
            c.Property(x => x.Grade).HasColumnName("grade").HasMaxLength(text);
            c.Property(x => x.Benchmark).HasColumnName("benchmark").HasMaxLength(text);
            c.Property(x => x.Responsibilities).HasColumnName("responsibilities").HasColumnType("jsonb")
                .HasConversion(Persistence.JsonColumn.Converter<string>(), Persistence.JsonColumn.Comparer<string>());
            c.Property(x => x.Competencies).HasColumnName("competencies").HasColumnType("jsonb")
                .HasConversion(Persistence.JsonColumn.Converter<string>(), Persistence.JsonColumn.Comparer<string>());
            c.Property(x => x.Assessments).HasColumnName("assessments").HasColumnType("jsonb")
                .HasConversion(Persistence.JsonColumn.Converter<string>(), Persistence.JsonColumn.Comparer<string>());
        });

        builder.HasIndex(j => new { j.TenantId, j.RequisitionId }).IsUnique();
        builder.HasIndex(j => new { j.TenantId, j.ReqId });
    }
}

internal sealed class ReqIdSequenceConfiguration : IEntityTypeConfiguration<ReqIdSequence>
{
    public void Configure(EntityTypeBuilder<ReqIdSequence> builder)
    {
        builder.ToTable("req_id_sequences");
        builder.HasKey(s => new { s.TenantId, s.Year });
    }
}
