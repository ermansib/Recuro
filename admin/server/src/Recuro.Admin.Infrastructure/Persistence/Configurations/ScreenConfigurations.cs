using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recuro.Admin.Domain.Screens;
using Recuro.Admin.Domain.Tenants;

namespace Recuro.Admin.Infrastructure.Persistence.Configurations;

internal sealed class ScreenDefinitionConfiguration : IEntityTypeConfiguration<ScreenDefinition>
{
    public void Configure(EntityTypeBuilder<ScreenDefinition> builder)
    {
        builder.ToTable("screen_definitions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Key).HasMaxLength(60).IsRequired();
        builder.HasIndex(s => s.Key).IsUnique();
        builder.Property(s => s.Code).HasMaxLength(10);
        builder.Property(s => s.Module).HasMaxLength(60);
        builder.Property(s => s.DefaultTitle).HasMaxLength(TenantScreenConfiguration.TextMaxLength);
        builder.Property(s => s.DefaultSubtitle).HasMaxLength(200);
        builder.OwnsMany(s => s.Fields, field =>
        {
            field.ToTable("field_definitions");
            field.WithOwner().HasForeignKey("ScreenDefinitionId");
            field.Property<int>("Id");
            field.HasKey("Id");
            field.Property(f => f.Key).HasMaxLength(60).IsRequired();
            field.Property(f => f.DefaultLabel).HasMaxLength(TenantScreenConfiguration.TextMaxLength);
            field.Property(f => f.DataType).HasConversion<string>().HasMaxLength(20);
        });
        builder.Navigation(s => s.Fields).HasField("_fields");
    }
}

internal sealed class TenantScreenConfigurationConfiguration : IEntityTypeConfiguration<TenantScreenConfiguration>
{
    public void Configure(EntityTypeBuilder<TenantScreenConfiguration> builder)
    {
        builder.ToTable("tenant_screen_configurations");
        builder.HasKey(c => c.Id);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(c => c.ScreenKey).HasMaxLength(60).IsRequired();
        builder.HasIndex(c => new { c.TenantId, c.ScreenKey }).IsUnique();
        builder.Property(c => c.Title).HasMaxLength(TenantScreenConfiguration.TextMaxLength);
        builder.Property(c => c.Subtitle).HasMaxLength(TenantScreenConfiguration.TextMaxLength);
        builder.OwnsMany(c => c.Fields, field =>
        {
            field.ToTable("tenant_field_settings");
            field.WithOwner().HasForeignKey("TenantScreenConfigurationId");
            field.Property<int>("Id");
            field.HasKey("Id");
            field.Property(f => f.FieldKey).HasMaxLength(60).IsRequired();
            field.Property(f => f.Label).HasMaxLength(TenantScreenConfiguration.TextMaxLength);
        });
        builder.Navigation(c => c.Fields).HasField("_fields");
    }
}
