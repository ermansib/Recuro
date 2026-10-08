using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Recuro.Onboarding.Application.Cases;
using Recuro.Onboarding.Domain.Bgv;
using Recuro.Onboarding.Domain.Cases;

namespace Recuro.Onboarding.Infrastructure.Persistence.Configurations;

internal sealed class OnboardingCaseConfiguration : IEntityTypeConfiguration<OnboardingCase>
{
    public void Configure(EntityTypeBuilder<OnboardingCase> builder)
    {
        builder.ToTable("onboarding_cases");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.AppId).HasMaxLength(OnboardingLimits.AppIdLength);
        builder.Property(c => c.OfferId).HasMaxLength(OnboardingLimits.OfferIdLength);
        builder.Property(c => c.ReqId).HasMaxLength(OnboardingLimits.ReqIdLength);
        builder.Property(c => c.CandidateId).HasMaxLength(OnboardingLimits.CandidateIdLength);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.RulesVersionId).HasMaxLength(OnboardingLimits.VersionIdLength);
        builder.Property(c => c.ReportingManagerId).HasMaxLength(OnboardingLimits.PersonIdLength);
        builder.Property(c => c.ReportingManager).HasMaxLength(OnboardingLimits.ActorLength);
        builder.Property(c => c.Buddy).HasMaxLength(OnboardingLimits.ActorLength);
        builder.Property(c => c.FileCompletedBy).HasMaxLength(OnboardingLimits.ActorLength);
        builder.Property(c => c.CancelReason).HasMaxLength(OnboardingLimits.ReasonLength);
        builder.Ignore(c => c.IsClosed);
        builder.Ignore(c => c.ChecklistPercent);

        // Two HR users on one case (a checklist tick and a decision): the second fails instead of overwriting.
        builder.Property<uint>("Version").IsRowVersion();

        builder.OwnsMany(c => c.Checklist, item =>
        {
            item.ToTable("onboarding_checklist_items");
            item.WithOwner().HasForeignKey("CaseId");
            item.Property<int>("Id");
            item.HasKey("Id");
            item.Property(x => x.Key).HasMaxLength(OnboardingLimits.KeyLength);
            item.Property(x => x.Label).HasMaxLength(OnboardingLimits.LabelLength);
            item.Property(x => x.Remarks).HasMaxLength(OnboardingLimits.RemarksLength);
            item.Property(x => x.UpdatedBy).HasMaxLength(OnboardingLimits.ActorLength);
        });
        builder.Navigation(c => c.Checklist).HasField("_checklist").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(c => c.Documents, document =>
        {
            document.ToTable("onboarding_documents");
            document.WithOwner().HasForeignKey("CaseId");
            document.Property<int>("Id");
            document.HasKey("Id");
            document.Property(x => x.Type).HasMaxLength(OnboardingLimits.KeyLength);
            document.Property(x => x.Label).HasMaxLength(OnboardingLimits.LabelLength);
            document.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            document.Property(x => x.FileName).HasMaxLength(OnboardingLimits.FileNameLength);
            document.Property(x => x.ContentType).HasMaxLength(OnboardingLimits.ContentTypeLength);
            document.Property(x => x.StorageKey).HasMaxLength(OnboardingLimits.StorageKeyLength);
            document.Property(x => x.UploadedBy).HasMaxLength(OnboardingLimits.ActorLength);
            document.Property(x => x.ReviewedBy).HasMaxLength(OnboardingLimits.ActorLength);
            document.Property(x => x.Note).HasMaxLength(OnboardingLimits.NoteLength);
            document.Ignore(x => x.IsVerified);
        });
        builder.Navigation(c => c.Documents).HasField("_documents").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(c => c.Milestones, milestone =>
        {
            milestone.ToTable("onboarding_milestones");
            milestone.WithOwner().HasForeignKey("CaseId");
            milestone.HasKey(x => x.Id);
            milestone.Property(x => x.Id).ValueGeneratedNever();
            milestone.Property(x => x.Kind).HasMaxLength(OnboardingLimits.KeyLength);
            milestone.Property(x => x.Label).HasMaxLength(OnboardingLimits.LabelLength);
            milestone.Property(x => x.Phase).HasConversion<string>().HasMaxLength(20);
            milestone.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            milestone.Property(x => x.CompletedBy).HasMaxLength(OnboardingLimits.ActorLength);
            milestone.Property(x => x.Notes).HasMaxLength(OnboardingLimits.ReasonLength + OnboardingLimits.LabelLength);
            milestone.Property(x => x.TicketRef).HasMaxLength(OnboardingLimits.TicketRefLength);
            milestone.Ignore(x => x.IsOpen);

            // The scheduler looks for scheduled milestones by due date.
            milestone.HasIndex(x => new { x.Status, x.DueOn });
        });
        builder.Navigation(c => c.Milestones).HasField("_milestones").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(c => c.Decisions, decision =>
        {
            decision.ToTable("onboarding_probation_decisions");
            decision.WithOwner().HasForeignKey("CaseId");
            decision.Property<int>("Id");
            decision.HasKey("Id");
            decision.Property(x => x.Outcome).HasConversion<string>().HasMaxLength(20);
            decision.Property(x => x.Reason).HasMaxLength(OnboardingLimits.ReasonLength);
            decision.Property(x => x.DecidedBy).HasMaxLength(OnboardingLimits.ActorLength);
            decision.Property(x => x.DecidedById).HasMaxLength(OnboardingLimits.PersonIdLength);
        });
        builder.Navigation(c => c.Decisions).HasField("_decisions").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(c => new { c.TenantId, c.AppId });
        builder.HasIndex(c => new { c.TenantId, c.Status, c.JoiningDate });
    }
}

internal sealed class BgvTrackConfiguration : IEntityTypeConfiguration<BgvTrack>
{
    public void Configure(EntityTypeBuilder<BgvTrack> builder)
    {
        builder.ToTable("bgv_tracks");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.AppId).HasMaxLength(OnboardingLimits.AppIdLength);
        builder.Property(t => t.CaseId).HasMaxLength(OnboardingLimits.OfferIdLength);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(t => new { t.TenantId, t.AppId }).IsUnique();
    }
}
