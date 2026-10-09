using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Recuro.Admin.Domain.Tenants;

namespace Recuro.Admin.Infrastructure.Persistence.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Name).HasMaxLength(Tenant.NameMaxLength).IsRequired();
        builder.Property(t => t.Slug).HasMaxLength(Tenant.SlugMaxLength).IsRequired();
        builder.HasIndex(t => t.Slug).IsUnique();
        builder.Property(t => t.CustomDomain).HasMaxLength(253);
        builder.HasIndex(t => t.CustomDomain).IsUnique();
        builder.Property(t => t.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Plan).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.ThemePresetKey).HasMaxLength(60).IsRequired();
        builder.Property(t => t.ThemeMode).HasConversion<string>().HasMaxLength(10);

        // Workspace settings (RCU-PLT-001). Defaults keep tenants created before sign-up existed valid.
        builder.Property(t => t.OrgType).HasConversion<string>().HasMaxLength(20).HasDefaultValue(OrgType.SmallBusiness);
        builder.Property(t => t.EmailDomain).HasMaxLength(Tenant.EmailDomainMaxLength);
        builder.Property(t => t.CareersTagline).HasMaxLength(200).HasDefaultValue(string.Empty);
        builder.Property(t => t.SsoProviders).HasConversion(KeyListConverter, KeyListComparer).HasMaxLength(200);
        builder.Property(t => t.MfaRoles).HasConversion(KeyListConverter, KeyListComparer).HasMaxLength(200);
        builder.Property(t => t.Locale).HasMaxLength(20).HasDefaultValue(WorkspaceDefaults.Locale);
        builder.Property(t => t.Currency).HasMaxLength(3).HasDefaultValue(WorkspaceDefaults.Currency);
        builder.Property(t => t.SessionIdleMinutes).HasDefaultValue(WorkspaceDefaults.SessionIdleMinutes);
    }

    /// <summary>Short lists of keys (sso providers, role keys) as one comma-separated column, on Postgres and SQLite alike.</summary>
    private static readonly ValueConverter<IReadOnlyList<string>, string> KeyListConverter = new(
        keys => string.Join(',', keys),
        column => column.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    private static readonly ValueComparer<IReadOnlyList<string>> KeyListComparer = new(
        (a, b) => (a ?? Array.Empty<string>()).SequenceEqual(b ?? Array.Empty<string>()),
        keys => keys.Aggregate(0, (hash, key) => HashCode.Combine(hash, key.GetHashCode(StringComparison.Ordinal))),
        keys => keys.ToArray());
}
