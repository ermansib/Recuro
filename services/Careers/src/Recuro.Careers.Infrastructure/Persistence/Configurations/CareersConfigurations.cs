using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recuro.Careers.Application.Postings;
using Recuro.Careers.Domain.Applications;
using Recuro.Careers.Domain.Postings;
using Recuro.Careers.Domain.Requisitions;

namespace Recuro.Careers.Infrastructure.Persistence.Configurations;

internal sealed class JobPostingConfiguration : IEntityTypeConfiguration<JobPosting>
{
    public void Configure(EntityTypeBuilder<JobPosting> builder)
    {
        builder.ToTable("job_postings");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.PostingId).HasMaxLength(CareersLimits.PostingIdLength);
        builder.Property(p => p.ReqId).HasMaxLength(CareersLimits.ReqIdLength);
        builder.Property(p => p.Title).HasMaxLength(CareersLimits.TitleLength);
        builder.Property(p => p.Location).HasMaxLength(CareersLimits.LocationLength);
        builder.Property(p => p.LocationFilter).HasMaxLength(CareersLimits.LocationLength);
        builder.Property(p => p.Experience).HasMaxLength(CareersLimits.ShortTextLength);
        builder.Property(p => p.Qualification).HasMaxLength(CareersLimits.ShortTextLength);
        builder.Property(p => p.Industry).HasMaxLength(CareersLimits.ShortTextLength);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property<uint>("Version").IsRowVersion();

        builder.OwnsMany(p => p.Tags, tag =>
        {
            tag.ToTable("job_posting_tags");
            tag.WithOwner().HasForeignKey("JobPostingId");
            tag.Property<int>("Id");
            tag.HasKey("Id");
            tag.Property(t => t.Text).HasMaxLength(CareersLimits.TagTextLength);
            tag.Property(t => t.Tone).HasMaxLength(CareersLimits.ToneLength);
        });
        builder.Navigation(p => p.Tags).HasField("_tags").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(p => p.History, entry =>
        {
            entry.ToTable("job_posting_history");
            entry.WithOwner().HasForeignKey("JobPostingId");
            entry.Property<int>("Id");
            entry.HasKey("Id");
            entry.Property(h => h.Action).HasConversion<string>().HasMaxLength(20);
            entry.Property(h => h.By).HasMaxLength(CareersLimits.ActorLength);
            entry.Property(h => h.ById).HasMaxLength(CareersLimits.ActorLength);
            entry.Property(h => h.Reason).HasMaxLength(CareersLimits.ReasonLength);
        });
        builder.Navigation(p => p.History).HasField("_history").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(p => new { p.TenantId, p.ReqId }).IsUnique();
        builder.HasIndex(p => new { p.TenantId, p.PostingId }).IsUnique();

        // RCU-CAR-001: the public search reads published postings newest first.
        builder.HasIndex(p => new { p.TenantId, p.VisibleFrom, p.PostingId }).HasFilter("status = 'Published'");
    }
}

internal sealed class PublicApplicationConfiguration : IEntityTypeConfiguration<PublicApplication>
{
    public void Configure(EntityTypeBuilder<PublicApplication> builder)
    {
        builder.ToTable("public_applications");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.PostingId).HasMaxLength(CareersLimits.PostingIdLength);
        builder.Property(a => a.ReqId).HasMaxLength(CareersLimits.ReqIdLength);
        builder.Property(a => a.Position).HasMaxLength(CareersLimits.TitleLength);
        builder.Property(a => a.ClientKey).HasMaxLength(CareersLimits.ClientKeyLength);
        builder.Property(a => a.CandidateId).HasMaxLength(CareersLimits.CandidateIdLength);
        builder.Property(a => a.DuplicateOf).HasMaxLength(CareersLimits.CandidateIdLength);
        builder.Property(a => a.AppId).HasMaxLength(CareersLimits.AppIdLength);
        builder.Property(a => a.FailureCode).HasMaxLength(CareersLimits.CodeLength);
        builder.Property(a => a.PipelineStage).HasMaxLength(20);
        builder.Property(a => a.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.PublicStage).HasConversion<string>().HasMaxLength(20);

        builder.OwnsMany(a => a.Consents, consent =>
        {
            consent.ToTable("public_application_consents");
            consent.WithOwner().HasForeignKey("ApplicationId");
            consent.Property<int>("Id");
            consent.HasKey("Id");
            consent.Property(c => c.Type).HasMaxLength(CareersLimits.ConsentTypeLength);
            consent.Property(c => c.TextVersion).HasMaxLength(CareersLimits.TextVersionLength);
        });
        builder.Navigation(a => a.Consents).HasField("_consents").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(a => new { a.TenantId, a.AppId }).IsUnique().HasFilter("app_id IS NOT NULL");
        builder.HasIndex(a => new { a.TenantId, a.ClientKey }).IsUnique().HasFilter("client_key IS NOT NULL");
        builder.HasIndex(a => a.FinalRejectedEventId).HasFilter("final_rejected_event_id IS NOT NULL");
    }
}

internal sealed class SourcingGateConfiguration : IEntityTypeConfiguration<SourcingGate>
{
    public void Configure(EntityTypeBuilder<SourcingGate> builder)
    {
        builder.ToTable("sourcing_gates");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.ReqId).HasMaxLength(CareersLimits.ReqIdLength);
        builder.HasIndex(g => new { g.TenantId, g.ReqId }).IsUnique();
    }
}
