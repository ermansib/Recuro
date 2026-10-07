using FluentValidation;
using Recuro.Candidate.Domain.Candidates;

namespace Recuro.Candidate.Application.Candidates.Commands.CreateCandidate;

internal sealed class CreateCandidateCommandValidator : AbstractValidator<CreateCandidateCommand>
{
    public CreateCandidateCommandValidator(TimeProvider clock)
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(CandidateLimits.NameLength);
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(CandidateLimits.EmailLength);
        RuleFor(c => c.Phone).MaximumLength(CandidateLimits.PhoneLength)
            .Matches(@"^[+0-9 ()\-]*$").WithMessage("Phone may contain digits, spaces, +, - and brackets only.");
        RuleFor(c => c.ExperienceYears).InclusiveBetween(0, CandidateLimits.MaxExperienceYears);
        RuleFor(c => c.Summary).MaximumLength(CandidateLimits.SummaryLength);
        RuleFor(c => c.CurrentCtc).InclusiveBetween(0, CandidateLimits.MaxCtc);
        RuleFor(c => c.ExpectedCtc).InclusiveBetween(0, CandidateLimits.MaxCtc);
        RuleFor(c => c.NoticeDays).InclusiveBetween(0, CandidateLimits.MaxNoticeDays);
        RuleFor(c => c.Source)
            .Must(s => CandidateSourceNames.TryParse(s, out _))
            .WithMessage($"Source must be one of: {string.Join(", ", CandidateSourceNames.All)}.");
        RuleFor(c => c.SourceRef).MaximumLength(CandidateLimits.RefLength);
        RuleFor(c => c.ReferrerId).MaximumLength(CandidateLimits.RefLength);
        RuleFor(c => c.Channel).MaximumLength(CandidateLimits.RefLength);
        RuleFor(c => c.ConsultantId).MaximumLength(CandidateLimits.RefLength)
            .NotEmpty().When(c => CandidateSourceNames.TryParse(c.Source, out var s) && s == CandidateSource.Consultant)
            .WithMessage("A consultant-sourced candidate needs the consultant's vendor id.");
        RuleFor(c => c.ConsentSource).NotEmpty().MaximumLength(CandidateLimits.ConsentSourceLength);
        RuleFor(c => c.Consents).NotNull();
        RuleForEach(c => c.Consents).ChildRules(consent =>
        {
            consent.RuleFor(c => c.Type).IsEnumName(typeof(ConsentType), caseSensitive: true);
            consent.RuleFor(c => c.TextVersion).NotEmpty().MaximumLength(CandidateLimits.TextVersionLength);
            consent.RuleFor(c => c.At).Must(at => at is null || at <= clock.GetUtcNow().AddMinutes(5))
                .WithMessage("Consent time cannot be in the future.");
        });
    }
}
