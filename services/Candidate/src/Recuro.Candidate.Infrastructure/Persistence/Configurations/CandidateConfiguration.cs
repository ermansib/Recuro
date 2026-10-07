using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Recuro.Candidate.Application.Candidates;
using Recuro.Candidate.Infrastructure.Pii;
using CandidateEntity = Recuro.Candidate.Domain.Candidates.Candidate;

namespace Recuro.Candidate.Infrastructure.Persistence.Configurations;

/// <summary>
/// Personal fields (name, email, phone, summary, CTC) are stored encrypted (BNFR-3); only the blind-index
/// fingerprints are searchable. Encrypted columns are text because ciphertext is longer than the input.
/// </summary>
internal sealed class CandidateConfiguration(IPiiCipher cipher) : IEntityTypeConfiguration<CandidateEntity>
{
    public void Configure(EntityTypeBuilder<CandidateEntity> builder)
    {
        var encrypted = new ValueConverter<string, string>(v => cipher.Encrypt(v), v => cipher.Decrypt(v));
        var encryptedAmount = new ValueConverter<decimal, string>(
            v => cipher.Encrypt(v.ToString(CultureInfo.InvariantCulture)),
            v => decimal.Parse(cipher.Decrypt(v), CultureInfo.InvariantCulture));

        builder.ToTable("candidates");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasConversion(encrypted);
        builder.Property(c => c.Email).HasConversion(encrypted);
        builder.Property(c => c.Phone).HasConversion(encrypted!);
        builder.Property(c => c.Summary).HasConversion(encrypted);
        builder.Property(c => c.CurrentCtc).HasConversion(encryptedAmount!);
        builder.Property(c => c.ExpectedCtc).HasConversion(encryptedAmount!);
        builder.Property(c => c.EmailFingerprint).HasMaxLength(64).IsFixedLength();
        builder.Property(c => c.PhoneFingerprint).HasMaxLength(64).IsFixedLength();
        builder.Property(c => c.ExperienceYears).HasPrecision(4, 1);
        builder.Property(c => c.LegalHoldReason).HasMaxLength(CandidateLimits.ReasonLength);
        builder.Property(c => c.RetentionStatus).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(c => c.IsPurged);

        builder.OwnsOne(c => c.Attribution, a =>
        {
            a.Property(x => x.Source).HasColumnName("source").HasConversion<string>().HasMaxLength(20);
            a.Property(x => x.SourceRef).HasColumnName("source_ref").HasMaxLength(CandidateLimits.RefLength);
            a.Property(x => x.ReferrerId).HasColumnName("referrer_id").HasMaxLength(CandidateLimits.RefLength);
            a.Property(x => x.ConsultantId).HasColumnName("consultant_id").HasMaxLength(CandidateLimits.RefLength);
            a.Property(x => x.Channel).HasColumnName("channel").HasMaxLength(CandidateLimits.RefLength);
        });
        builder.Navigation(c => c.Attribution).IsRequired();

        builder.OwnsOne(c => c.Resume, r =>
        {
            r.Property(x => x.FileName).HasColumnName("resume_file_name").HasConversion(encrypted);
            r.Property(x => x.ContentType).HasColumnName("resume_content_type").HasMaxLength(100);
            r.Property(x => x.SizeBytes).HasColumnName("resume_size_bytes");
            r.Property(x => x.StorageKey).HasColumnName("resume_storage_key").HasMaxLength(200);
            r.Property(x => x.UploadedAt).HasColumnName("resume_uploaded_at");
        });

        builder.OwnsMany(c => c.Consents, consent =>
        {
            consent.ToTable("candidate_consents");
            consent.WithOwner().HasForeignKey("CandidateId");
            consent.Property<int>("Id");
            consent.HasKey("Id");
            consent.Property(x => x.Type).HasConversion<string>().HasMaxLength(40);
            consent.Property(x => x.TextVersion).HasMaxLength(CandidateLimits.TextVersionLength);
            consent.Property(x => x.Source).HasMaxLength(CandidateLimits.ConsentSourceLength);
        });
        builder.Navigation(c => c.Consents).HasField("_consents").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(c => c.Applications, application =>
        {
            application.ToTable("candidate_applications");
            application.WithOwner().HasForeignKey("CandidateId");
            application.HasKey("CandidateId", nameof(Domain.Candidates.CandidateApplication.AppId));
            application.Property(x => x.AppId).HasMaxLength(40);
            application.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20);
        });
        builder.Navigation(c => c.Applications).HasField("_applications").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(c => new { c.TenantId, c.EmailFingerprint });
        builder.HasIndex(c => new { c.TenantId, c.PhoneFingerprint });
        builder.HasIndex(c => new { c.TenantId, c.RetentionStatus, c.RetainUntil });
    }
}
