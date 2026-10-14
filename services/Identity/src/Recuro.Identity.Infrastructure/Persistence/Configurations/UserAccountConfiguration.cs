using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recuro.Identity.Application.Users;
using Recuro.Identity.Domain.Users;

namespace Recuro.Identity.Infrastructure.Persistence.Configurations;

internal sealed class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public const string RolesField = "_roles";

    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Subject).HasMaxLength(UserLimits.SubjectLength);
        builder.Property(u => u.Name).HasMaxLength(UserLimits.NameLength);
        builder.Property(u => u.Email).HasMaxLength(UserLimits.EmailLength);
        builder.Property(u => u.Department).HasMaxLength(UserPlacement.DepartmentLength).HasDefaultValue(string.Empty);
        builder.Property(u => u.ManagerId).HasMaxLength(UserPlacement.ManagerIdLength).HasDefaultValue(string.Empty);
        builder.Ignore(u => u.Roles);
        builder.Property<List<string>>(RolesField).HasColumnName("roles").HasColumnType("text[]");

        // One mirror row per person per tenant; also the lookup on every sign-in.
        builder.HasIndex(u => new { u.TenantId, u.Subject }).IsUnique();

        // A department's head and people (RCU-ONB-005 HOD lookup).
        builder.HasIndex(u => new { u.TenantId, u.Department });
    }
}
