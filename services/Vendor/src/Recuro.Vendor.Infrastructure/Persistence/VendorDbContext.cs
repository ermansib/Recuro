using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Infrastructure.Persistence;
using Recuro.Vendor.Application.Vendors;
using VendorEntity = Recuro.Vendor.Domain.Vendors.Vendor;

namespace Recuro.Vendor.Infrastructure.Persistence;

/// <summary>The Vendor service's own database (recuro_vendor). No other service reads it.</summary>
public sealed class VendorDbContext(DbContextOptions<VendorDbContext> options, ITenantContext tenant, IDomainEventDispatcher domainEvents)
    : RecuroDbContext(options, tenant, domainEvents)
{
    public DbSet<VendorEntity> Vendors => Set<VendorEntity>();

    protected override void ConfigureModel(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(VendorDbContext).Assembly);
}

internal sealed class VendorConfiguration : IEntityTypeConfiguration<VendorEntity>
{
    public void Configure(EntityTypeBuilder<VendorEntity> builder)
    {
        builder.ToTable("vendors");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Name).HasMaxLength(VendorLimits.NameLength);
        builder.Property(v => v.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(v => v.CreatedBy).HasMaxLength(VendorLimits.ActorLength);
        builder.Property(v => v.ApprovedBy).HasMaxLength(VendorLimits.ActorLength);
        builder.Property(v => v.DeEmpanelReason).HasMaxLength(VendorLimits.ReasonLength);
        builder.Ignore(v => v.IsActive);
        builder.Property<uint>("Version").IsRowVersion();

        builder.OwnsOne(v => v.Fee, fee =>
        {
            fee.Property(x => x.Kind).HasColumnName("fee_kind").HasConversion<string>().HasMaxLength(20);
            fee.Property(x => x.Value).HasColumnName("fee_value").HasPrecision(12, 2);
            fee.Property(x => x.Justification).HasColumnName("fee_justification").HasMaxLength(VendorLimits.JustificationLength);
            fee.Property(x => x.TermsVersion).HasColumnName("fee_terms_version").HasMaxLength(VendorLimits.TermsVersionLength);
        });
        builder.Navigation(v => v.Fee).IsRequired();

        builder.OwnsOne(v => v.Gates, gates =>
        {
            gates.Property(x => x.Experience).HasColumnName("gate_experience");
            gates.Property(x => x.TrackRecord).HasColumnName("gate_track_record");
            gates.Property(x => x.Agreement).HasColumnName("gate_agreement");
            gates.Property(x => x.Nda).HasColumnName("gate_nda");
            gates.Property(x => x.ReplacementGuarantee).HasColumnName("gate_replacement_guarantee");
            gates.Property(x => x.PrivacyAck).HasColumnName("gate_privacy_ack");
        });
        builder.Navigation(v => v.Gates).IsRequired();

        builder.OwnsOne(v => v.Documents, docs =>
        {
            docs.Property(x => x.NdaRef).HasColumnName("nda_ref").HasMaxLength(VendorLimits.DocumentRefLength);
            docs.Property(x => x.AgreementRef).HasColumnName("agreement_ref").HasMaxLength(VendorLimits.DocumentRefLength);
            docs.Property(x => x.PrivacyAckRef).HasColumnName("privacy_ack_ref").HasMaxLength(VendorLimits.DocumentRefLength);
        });
        builder.Navigation(v => v.Documents).IsRequired();

        builder.HasIndex(v => new { v.TenantId, v.Name }).IsUnique();
        builder.HasIndex(v => new { v.TenantId, v.Type, v.Status });
    }
}
