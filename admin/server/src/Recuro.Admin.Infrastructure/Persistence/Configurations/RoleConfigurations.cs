using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recuro.Admin.Domain.Roles;
using Recuro.Admin.Domain.Tenants;

namespace Recuro.Admin.Infrastructure.Persistence.Configurations;

internal sealed class WorkspaceRoleConfiguration : IEntityTypeConfiguration<WorkspaceRole>
{
    public void Configure(EntityTypeBuilder<WorkspaceRole> builder)
    {
        builder.ToTable("workspace_roles");
        builder.HasKey(r => r.Id);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(r => r.Key).HasMaxLength(WorkspaceRole.KeyMaxLength).IsRequired();
        builder.HasIndex(r => new { r.TenantId, r.Key }).IsUnique();
        builder.Property(r => r.Name).HasMaxLength(WorkspaceRole.NameMaxLength).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(WorkspaceRole.DescriptionMaxLength);
        builder.Property(r => r.ReportsTo).HasMaxLength(WorkspaceRole.KeyMaxLength);
        builder.Property(r => r.Permissions).HasConversion(KeyList.Converter, KeyList.Comparer).HasMaxLength(2000);
        builder.Property(r => r.Pages).HasConversion(KeyList.Converter, KeyList.Comparer).HasMaxLength(1000);
    }
}

internal sealed class WorkspaceMemberConfiguration : IEntityTypeConfiguration<WorkspaceMember>
{
    public void Configure(EntityTypeBuilder<WorkspaceMember> builder)
    {
        builder.ToTable("workspace_members");
        builder.HasKey(m => m.Id);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(m => m.AccountId).HasMaxLength(100).IsRequired();
        builder.HasIndex(m => new { m.TenantId, m.AccountId }).IsUnique();
        builder.Property(m => m.Name).HasMaxLength(WorkspaceMember.NameMaxLength).IsRequired();
        builder.Property(m => m.Email).HasMaxLength(WorkspaceMember.EmailMaxLength).IsRequired();
        builder.Property(m => m.RoleKeys).HasConversion(KeyList.Converter, KeyList.Comparer).HasMaxLength(1000);
        builder.Property(m => m.Department).HasMaxLength(WorkspaceMember.DepartmentMaxLength);
    }
}
