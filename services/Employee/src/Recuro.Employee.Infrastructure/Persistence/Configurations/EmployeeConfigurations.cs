using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recuro.Employee.Application;
using Recuro.Employee.Domain.Ijp;
using Recuro.Employee.Domain.Intake;
using Recuro.Employee.Domain.Referrals;
using Recuro.Employee.Domain.Requisitions;

namespace Recuro.Employee.Infrastructure.Persistence.Configurations;

internal sealed class IjpPostingConfiguration : IEntityTypeConfiguration<IjpPosting>
{
    public void Configure(EntityTypeBuilder<IjpPosting> builder)
    {
        builder.ToTable("ijp_postings");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.ReqId).HasMaxLength(EmployeeLimits.ReqIdLength);
        builder.Property(p => p.Title).HasMaxLength(EmployeeLimits.TitleLength);
        builder.Property(p => p.Location).HasMaxLength(EmployeeLimits.ShortTextLength);
        builder.Property(p => p.Department).HasMaxLength(EmployeeLimits.ShortTextLength);
        builder.Property(p => p.Grade).HasMaxLength(EmployeeLimits.GradeLength);
        builder.Property(p => p.Summary).HasMaxLength(EmployeeLimits.SummaryLength);
        builder.Property(p => p.CreatedBy).HasMaxLength(EmployeeLimits.NameLength);
        builder.PrimitiveCollection(p => p.EligibleGrades).HasField("_eligibleGrades").UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasIndex(p => new { p.TenantId, p.ReqId }).IsUnique();

        // RCU-EMP-001: the listing reads postings whose window is open.
        builder.HasIndex(p => new { p.TenantId, p.ClosesAt }).HasFilter("NOT withdrawn");
    }
}

/// <summary>IJP applications and referrals share one table (TPH): the saga and progress columns are common.</summary>
internal sealed class IntakeRecordConfiguration : IEntityTypeConfiguration<IntakeRecord>
{
    public void Configure(EntityTypeBuilder<IntakeRecord> builder)
    {
        builder.ToTable("intake_records");
        builder.HasKey(r => r.Id);
        builder.HasDiscriminator<string>("kind")
            .HasValue<InternalApplication>("ijp")
            .HasValue<Referral>("referral");
        builder.Property("kind").HasMaxLength(20);
        builder.Property(r => r.EmployeeId).HasMaxLength(EmployeeLimits.UserIdLength);
        builder.Property(r => r.EmployeeName).HasMaxLength(EmployeeLimits.NameLength);
        builder.Property(r => r.ReqId).HasMaxLength(EmployeeLimits.ReqIdLength);
        builder.Property(r => r.CandidateId).HasMaxLength(EmployeeLimits.CandidateIdLength);
        builder.Property(r => r.AppId).HasMaxLength(EmployeeLimits.AppIdLength);
        builder.Property(r => r.FailureCode).HasMaxLength(EmployeeLimits.CodeLength);
        builder.Property(r => r.PipelineStage).HasMaxLength(20);
        builder.Property(r => r.State).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Progress).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(r => new { r.TenantId, r.EmployeeId, r.SubmittedAt });
        builder.HasIndex(r => new { r.TenantId, r.AppId }).IsUnique().HasFilter("app_id IS NOT NULL");
    }
}

internal sealed class InternalApplicationConfiguration : IEntityTypeConfiguration<InternalApplication>
{
    public void Configure(EntityTypeBuilder<InternalApplication> builder)
    {
        builder.Property(a => a.PostingTitle).HasMaxLength(EmployeeLimits.TitleLength);
        builder.Property(a => a.DeclaredGrade).HasMaxLength(EmployeeLimits.GradeLength);
    }
}

internal sealed class ReferralConfiguration : IEntityTypeConfiguration<Referral>
{
    public void Configure(EntityTypeBuilder<Referral> builder)
    {
        builder.Property(r => r.CandidateName).HasMaxLength(EmployeeLimits.NameLength);
        builder.Property(r => r.Relationship).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.DuplicateOf).HasMaxLength(EmployeeLimits.CandidateIdLength);
    }
}

internal sealed class SourcingGateConfiguration : IEntityTypeConfiguration<SourcingGate>
{
    public void Configure(EntityTypeBuilder<SourcingGate> builder)
    {
        builder.ToTable("sourcing_gates");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.ReqId).HasMaxLength(EmployeeLimits.ReqIdLength);
        builder.HasIndex(g => new { g.TenantId, g.ReqId }).IsUnique();
    }
}
