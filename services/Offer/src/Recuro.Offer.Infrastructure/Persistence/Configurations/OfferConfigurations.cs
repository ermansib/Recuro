using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Recuro.Offer.Domain.Applications;
using Recuro.Offer.Domain.Bgv;
using Recuro.Offer.Domain.Letters;
using Recuro.Offer.Domain.Offers;

namespace Recuro.Offer.Infrastructure.Persistence.Configurations;

internal sealed class JobOfferConfiguration : IEntityTypeConfiguration<JobOffer>
{
    public void Configure(EntityTypeBuilder<JobOffer> builder)
    {
        builder.ToTable("offers");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.AppId).HasMaxLength(OfferLimits.IdLength);
        builder.Property(o => o.ReqId).HasMaxLength(OfferLimits.IdLength);
        builder.Property(o => o.CandidateId).HasMaxLength(OfferLimits.IdLength);
        builder.Property(o => o.CandidateName).HasMaxLength(OfferLimits.ShortText);
        builder.Property(o => o.Designation).HasMaxLength(OfferLimits.ShortText);
        builder.Property(o => o.Grade).HasMaxLength(OfferLimits.IdLength);
        builder.Property(o => o.Location).HasMaxLength(OfferLimits.ShortText);
        builder.Property(o => o.ReportingManager).HasMaxLength(OfferLimits.ShortText);
        builder.Property(o => o.State).HasConversion<string>().HasMaxLength(32);
        builder.Property(o => o.CreatedBy).HasMaxLength(OfferLimits.ShortText);
        builder.Property(o => o.Version).IsRowVersion();
        builder.Ignore(o => o.IsOpen);

        builder.ComplexProperty(o => o.Components, c =>
        {
            c.Property(x => x.Fixed).HasColumnName("ctc_fixed").HasPrecision(10, 1);
            c.Property(x => x.Variable).HasColumnName("ctc_variable").HasPrecision(10, 1);
            c.Property(x => x.Benefits).HasColumnName("ctc_benefits").HasPrecision(10, 1);
        });
        builder.ComplexProperty(o => o.Band, b =>
        {
            b.Property(x => x.Min).HasColumnName("band_min").HasPrecision(10, 1);
            b.Property(x => x.Max).HasColumnName("band_max").HasPrecision(10, 1);
        });

        builder.Property(o => o.Route)
            .HasColumnType("jsonb")
            .HasConversion(new ValueConverter<OfferRoute?, string?>(
                route => route == null ? null : JsonColumn.Serialize(route),
                json => json == null ? null : JsonColumn.Deserialize<OfferRoute>(json)));

        builder.Property<List<TrailEntry>>("_trail")
            .HasColumnName("trail")
            .HasColumnType("jsonb")
            .HasConversion(JsonColumn.ListConverter<TrailEntry>(), JsonColumn.ListComparer<TrailEntry>());
        builder.Ignore(o => o.Trail);

        builder.HasIndex(o => new { o.TenantId, o.AppId });
        builder.HasIndex(o => new { o.TenantId, o.CandidateId });
        builder.HasIndex(o => new { o.TenantId, o.State });
        builder.HasIndex(o => o.WorkflowInstanceId);

        // The lifecycle scanner's query: sent offers by their next due time.
        builder.HasIndex(o => new { o.State, o.NextChaseAt });
    }
}

internal sealed class OfferDocumentConfiguration : IEntityTypeConfiguration<OfferDocument>
{
    public void Configure(EntityTypeBuilder<OfferDocument> builder)
    {
        builder.ToTable("offer_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(d => d.ContentType).HasMaxLength(100);
        builder.HasIndex(d => new { d.TenantId, d.OfferId, d.Kind, d.LetterVersion }).IsUnique();
    }
}

internal sealed class ApplicationTrackConfiguration : IEntityTypeConfiguration<ApplicationTrack>
{
    public void Configure(EntityTypeBuilder<ApplicationTrack> builder)
    {
        builder.ToTable("application_tracks");
        builder.Property<Guid>("Id").ValueGeneratedOnAdd();
        builder.HasKey("Id");
        builder.Property(t => t.AppId).HasMaxLength(OfferLimits.IdLength);
        builder.Property(t => t.ReqId).HasMaxLength(OfferLimits.IdLength);
        builder.Property(t => t.CandidateId).HasMaxLength(OfferLimits.IdLength);
        builder.Property(t => t.Stage).HasMaxLength(32);
        builder.Ignore(t => t.OfferAllowed);
        builder.HasIndex(t => new { t.TenantId, t.AppId }).IsUnique();
    }
}

internal sealed class BgvTrackConfiguration : IEntityTypeConfiguration<BgvTrack>
{
    public void Configure(EntityTypeBuilder<BgvTrack> builder)
    {
        builder.ToTable("bgv_tracks");
        builder.Property<Guid>("Id").ValueGeneratedOnAdd();
        builder.HasKey("Id");
        builder.Property(t => t.AppId).HasMaxLength(OfferLimits.IdLength);
        builder.Property(t => t.Gate).HasConversion<string>().HasMaxLength(32);
        builder.Property(t => t.Blockers)
            .HasColumnType("jsonb")
            .HasConversion(JsonColumn.Converter<string>(), JsonColumn.Comparer<string>());
        builder.HasIndex(t => new { t.TenantId, t.AppId }).IsUnique();
    }
}
