using System.Text.RegularExpressions;
using FluentValidation;
using Recuro.Config.Domain.Rules;
using Recuro.Config.Domain.RuleSets;

namespace Recuro.Config.Application.RuleSets;

/// <summary>Business validation of each matrix type, after <see cref="MatrixJson"/> has checked its shape.</summary>
internal static partial class MatrixValidators
{
    public static FluentValidation.Results.ValidationResult Validate(MatrixType type, object matrix) => type switch
    {
        MatrixType.Doa => new DoaMatrixValidator().Validate((DoaMatrix)matrix),
        MatrixType.Tat => new TatMatrixValidator().Validate((TatMatrix)matrix),
        MatrixType.Offer => new OfferMatrixValidator().Validate((OfferMatrix)matrix),
        MatrixType.Escalation => new EscalationMatrixValidator().Validate((EscalationMatrix)matrix),
        MatrixType.Bgv => new BgvMatrixValidator().Validate((BgvMatrix)matrix),
        MatrixType.Calendar => new CalendarMatrixValidator().Validate((CalendarMatrix)matrix),
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    /// <summary>Role keys look like <c>hrhead</c> or <c>ta-head</c>.</summary>
    [GeneratedRegex("^[a-z][a-z0-9-]{0,49}$")]
    internal static partial Regex RoleKey();

    internal static IRuleBuilderOptions<T, string> MustBeRoleKey<T>(this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty().Must(r => RoleKey().IsMatch(r)).WithMessage("Role keys are lowercase letters, digits and dashes, e.g. hrhead.");

    internal static IRuleBuilderOptions<T, string> MustBePersona<T>(this IRuleBuilder<T, string> rule) =>
        rule.Must(r => ConfigLimits.PersonaRoles.Contains(r)).WithMessage("Must be one of: " + string.Join(", ", ConfigLimits.PersonaRoles) + ".");

    internal static IRuleBuilderOptions<T, string> Label<T>(this IRuleBuilder<T, string> rule) => rule.NotEmpty().MaximumLength(200);
}

internal sealed class DoaMatrixValidator : AbstractValidator<DoaMatrix>
{
    public DoaMatrixValidator()
    {
        RuleFor(m => m.Routes).NotEmpty()
            .Must(routes => routes.Select(r => r.Grade.ToUpperInvariant()).Distinct().Count() == routes.Count)
            .WithMessage("Each grade has one route.");
        RuleForEach(m => m.Routes).ChildRules(route =>
        {
            route.RuleFor(r => r.Grade).NotEmpty().MaximumLength(20);
            route.RuleFor(r => r.Initiating).Label();
            route.RuleFor(r => r.Recommending).Label();
            route.RuleFor(r => r.Approving).Label();
            route.RuleFor(r => r.ApproverRole).MustBePersona();
            route.RuleFor(r => r.BandLabel).Label();
            route.RuleFor(r => r.OverallTat.MinDays).GreaterThanOrEqualTo(0);
            route.RuleFor(r => r.OverallTat.MaxDays).GreaterThanOrEqualTo(r => r.OverallTat.MinDays);
            route.RuleFor(r => r.OverallTat.Label).Label();
            route.RuleFor(r => r.Legs).NotEmpty();
            route.RuleForEach(r => r.Legs).SetValidator(new ApprovalLegValidator());
            route.RuleForEach(r => r.OobLegs).SetValidator(new ApprovalLegValidator());
        });
    }
}

internal sealed class ApprovalLegValidator : AbstractValidator<ApprovalLeg>
{
    public ApprovalLegValidator()
    {
        RuleFor(l => l.Name).Label();
        RuleFor(l => l.Assignees).NotEmpty();
        RuleForEach(l => l.Assignees).ChildRules(a =>
        {
            a.RuleFor(x => x.Role).MustBeRoleKey();
            a.RuleFor(x => x.Label).Label();
        });
        RuleFor(l => l.SlaWorkingDays).InclusiveBetween(1, 365);
        RuleForEach(l => l.Escalation).ChildRules(e =>
        {
            e.RuleFor(x => x.Role).MustBeRoleKey();
            e.RuleFor(x => x.Label).Label();
            e.RuleFor(x => x.AfterWorkingDays).InclusiveBetween(0, 365);
        });
    }
}

internal sealed class TatMatrixValidator : AbstractValidator<TatMatrix>
{
    public TatMatrixValidator()
    {
        RuleFor(m => m.Stages).NotEmpty()
            .Must(s => s.Select(x => x.Stage).Distinct(StringComparer.OrdinalIgnoreCase).Count() == s.Count)
            .WithMessage("Each stage appears once.");
        RuleForEach(m => m.Stages).ChildRules(stage =>
        {
            stage.RuleFor(s => s.Stage).MustBeRoleKey().WithMessage("Stage keys are lowercase letters, digits and dashes, e.g. mrf-approval.");
            stage.RuleFor(s => s.Label).Label();
            stage.RuleFor(s => s.Owner).Label();
            stage.RuleFor(s => s.MinWorkingDays).GreaterThanOrEqualTo(0);
            stage.RuleFor(s => s.MaxWorkingDays).GreaterThanOrEqualTo(s => s.MinWorkingDays);
            stage.RuleFor(s => s.EscalateTo!.Role).MustBeRoleKey().When(s => s.EscalateTo is not null);
        });
    }
}

internal sealed class EscalationMatrixValidator : AbstractValidator<EscalationMatrix>
{
    public EscalationMatrixValidator()
    {
        RuleFor(m => m.Issues).NotEmpty()
            .Must(i => i.Select(x => x.Issue).Distinct(StringComparer.OrdinalIgnoreCase).Count() == i.Count)
            .WithMessage("Each issue type appears once.");
        RuleForEach(m => m.Issues).ChildRules(issue =>
        {
            issue.RuleFor(i => i.Issue).MustBeRoleKey().WithMessage("Issue keys are lowercase letters, digits and dashes, e.g. adverse-bgv.");
            issue.RuleFor(i => i.Label).Label();
            issue.RuleFor(i => i.First.Role).MustBeRoleKey();
            issue.RuleFor(i => i.First.Label).Label();
            issue.RuleFor(i => i.Final.Role).MustBeRoleKey();
            issue.RuleFor(i => i.Final.Label).Label();
        });
    }
}

internal sealed class OfferMatrixValidator : AbstractValidator<OfferMatrix>
{
    public OfferMatrixValidator()
    {
        RuleFor(m => m.Rules).NotEmpty()
            .Must(rules => rules.SelectMany(r => r.Levels).Distinct(StringComparer.OrdinalIgnoreCase).Count() == rules.Sum(r => r.Levels.Count))
            .WithMessage("Each grade belongs to one rule.");
        RuleForEach(m => m.Rules).ChildRules(rule =>
        {
            rule.RuleFor(r => r.Levels).NotEmpty();
            rule.RuleFor(r => r.WithinBand.Label).Label();
            rule.RuleFor(r => r.WithinBand.ApproverRole).MustBePersona();
            rule.RuleFor(r => r.Deviation.Label).Label();
            rule.RuleFor(r => r.Deviation.ApproverRole).MustBePersona();
        });
    }
}

internal sealed class BgvMatrixValidator : AbstractValidator<BgvMatrix>
{
    public BgvMatrixValidator()
    {
        RuleFor(m => m.Checks).NotEmpty()
            .Must(c => c.Select(x => x.Type).Distinct(StringComparer.Ordinal).Count() == c.Count)
            .WithMessage("Each check type appears once.");
        RuleForEach(m => m.Checks).ChildRules(check =>
        {
            check.RuleFor(c => c.Type).NotEmpty().MaximumLength(50);
            check.RuleFor(c => c.Label).Label();
            check.RuleFor(c => c.Detail).Label();
            check.RuleFor(c => c.Condition).Label();
        });
    }
}

internal sealed class CalendarMatrixValidator : AbstractValidator<CalendarMatrix>
{
    public CalendarMatrixValidator()
    {
        RuleFor(m => m.DefaultLocation).NotEmpty().MaximumLength(100);
        RuleFor(m => m.Calendars).NotEmpty()
            .Must(c => c.Select(x => x.Location).Distinct(StringComparer.OrdinalIgnoreCase).Count() == c.Count)
            .WithMessage("Each location has one calendar.");
        RuleFor(m => m).Must(m => m.Find(m.DefaultLocation) is not null)
            .WithName("defaultLocation")
            .WithMessage("defaultLocation must be one of the calendars.");
        RuleForEach(m => m.Calendars).ChildRules(calendar =>
        {
            calendar.RuleFor(c => c.Location).NotEmpty().MaximumLength(100);
            calendar.RuleFor(c => c.WeekendDays).Must(d => d.Distinct().Count() < 7).WithMessage("A week needs at least one working day.");
            calendar.RuleFor(c => c.Holidays).Must(h => h.Count <= 400).WithMessage("At most 400 holidays.");
        });
    }
}
