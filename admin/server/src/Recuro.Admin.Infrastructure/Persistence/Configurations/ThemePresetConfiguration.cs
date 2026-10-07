using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recuro.Admin.Domain.Theming;

namespace Recuro.Admin.Infrastructure.Persistence.Configurations;

internal sealed class ThemePresetConfiguration : IEntityTypeConfiguration<ThemePreset>
{
    public void Configure(EntityTypeBuilder<ThemePreset> builder)
    {
        builder.ToTable("theme_presets");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Key).HasMaxLength(60).IsRequired();
        builder.HasIndex(p => p.Key).IsUnique();
        builder.Property(p => p.Name).HasMaxLength(80).IsRequired();
        builder.OwnsOne(p => p.Light, palette => ConfigurePalette(palette, "light"));
        builder.OwnsOne(p => p.Dark, palette => ConfigurePalette(palette, "dark"));
    }

    private static void ConfigurePalette<TOwner>(OwnedNavigationBuilder<TOwner, ThemePalette> palette, string prefix)
        where TOwner : class
    {
        palette.Property(c => c.Primary).HasColumnName($"{prefix}_primary").HasMaxLength(7);
        palette.Property(c => c.Secondary).HasColumnName($"{prefix}_secondary").HasMaxLength(7);
        palette.Property(c => c.Accent).HasColumnName($"{prefix}_accent").HasMaxLength(7);
        palette.Property(c => c.Background).HasColumnName($"{prefix}_background").HasMaxLength(7);
        palette.Property(c => c.Surface).HasColumnName($"{prefix}_surface").HasMaxLength(7);
        palette.Property(c => c.Text).HasColumnName($"{prefix}_text").HasMaxLength(7);
    }
}
