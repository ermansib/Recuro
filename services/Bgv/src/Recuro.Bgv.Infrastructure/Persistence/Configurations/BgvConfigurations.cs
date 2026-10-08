using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recuro.Bgv.Application.Cases;
using Recuro.Bgv.Domain.Cases;
using Recuro.Bgv.Domain.Requests;

namespace Recuro.Bgv.Infrastructure.Persistence.Configurations;

internal sealed class BgvCaseConfiguration : IEntityTypeConfiguration<BgvCase>
{
    public void Configure(EntityTypeBuilder<BgvCase> builder)
    {
        builder.ToTable("bgv_cases");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.AppId).HasMaxLength(BgvLimits.AppIdLength);
        builder.Property(c => c.ReqId).HasMaxLength(BgvLimits.ReqIdLength);
        builder.Property(c => c.CandidateId).HasMaxLength(BgvLimits.CandidateIdLength);
        builder.Property(c => c.VendorId).HasMaxLength(BgvLimits.VendorIdLength);
        builder.Property(c => c.VendorName).HasMaxLength(BgvLimits.VendorNameLength);
        builder.Property(c => c.VendorCaseRef).HasMaxLength(BgvLimits.VendorCaseRefLength);
        builder.Property(c => c.Grade).HasMaxLength(10);
        builder.Property(c => c.Scope).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.MatrixVersionId).HasMaxLength(BgvLimits.VersionIdLength);
        builder.Property(c => c.InitiatedBy).HasMaxLength(BgvLimits.ActorLength);
        builder.Ignore(c => c.IsClosed);

        // Two updates at once (a vendor status and an adverse report): the second fails instead of overwriting.
        builder.Property<uint>("Version").IsRowVersion();

        builder.OwnsOne(c => c.Consent, consent =>
        {
            consent.Property(x => x.At).HasColumnName("consent_at");
            consent.Property(x => x.TextVersion).HasColumnName("consent_text_version").HasMaxLength(BgvLimits.ConsentTextVersionLength);
            consent.Property(x => x.Source).HasColumnName("consent_source").HasMaxLength(BgvLimits.ConsentSourceLength);
        });
        builder.Navigation(c => c.Consent).IsRequired();

        builder.OwnsOne(c => c.Adverse, adverse =>
        {
            adverse.Property(x => x.Check).HasColumnName("adverse_check").HasMaxLength(BgvLimits.CheckTypeLength);
            adverse.Property(x => x.Description).HasColumnName("adverse_description").HasMaxLength(BgvLimits.DescriptionLength);
            adverse.Property(x => x.Action).HasColumnName("adverse_action").HasConversion<string>().HasMaxLength(30);
            adverse.Property(x => x.FlaggedAt).HasColumnName("adverse_flagged_at");
            adverse.Property(x => x.FlaggedBy).HasColumnName("adverse_flagged_by").HasMaxLength(BgvLimits.ActorLength);
            adverse.Property(x => x.WorkflowId).HasColumnName("adverse_workflow_id");
        });

        builder.OwnsOne(c => c.Resolution, resolution =>
        {
            resolution.Property(x => x.Outcome).HasColumnName("resolution_outcome").HasConversion<string>().HasMaxLength(20);
            resolution.Property(x => x.Reason).HasColumnName("resolution_reason").HasMaxLength(BgvLimits.ReasonLength);
            resolution.Property(x => x.DecidedBy).HasColumnName("resolution_decided_by").HasMaxLength(BgvLimits.ActorLength);
            resolution.Property(x => x.DecidedAt).HasColumnName("resolution_decided_at");
        });

        builder.OwnsMany(c => c.Checks, check =>
        {
            check.ToTable("bgv_checks");
            check.WithOwner().HasForeignKey("CaseId");
            check.Property<int>("Id");
            check.HasKey("Id");
            check.Property(x => x.Type).HasMaxLength(BgvLimits.CheckTypeLength);
            check.Property(x => x.Label).HasMaxLength(BgvLimits.LabelLength);
            check.Property(x => x.Detail).HasMaxLength(BgvLimits.DetailLength);
            check.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            check.Property(x => x.Note).HasMaxLength(BgvLimits.NoteLength + BgvLimits.ReasonLength);
            check.Property(x => x.SensitiveNote).HasMaxLength(BgvLimits.SensitiveNoteLength);
        });
        builder.Navigation(c => c.Checks).HasField("_checks").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(c => new { c.TenantId, c.AppId }).IsUnique();
        builder.HasIndex(c => new { c.TenantId, c.VendorId, c.Status });
        builder.HasIndex(c => new { c.TenantId, c.Status, c.DueAt });
    }
}

internal sealed class BgvRequestConfiguration : IEntityTypeConfiguration<BgvRequest>
{
    public void Configure(EntityTypeBuilder<BgvRequest> builder)
    {
        builder.ToTable("bgv_requests");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.AppId).HasMaxLength(BgvLimits.AppIdLength);
        builder.Property(r => r.ReqId).HasMaxLength(BgvLimits.ReqIdLength);
        builder.Property(r => r.CandidateId).HasMaxLength(BgvLimits.CandidateIdLength);
        builder.Property(r => r.Source).HasMaxLength(BgvLimits.SourceLength);
        builder.Ignore(r => r.IsInternal);
        builder.HasIndex(r => new { r.TenantId, r.AppId }).IsUnique();
    }
}
