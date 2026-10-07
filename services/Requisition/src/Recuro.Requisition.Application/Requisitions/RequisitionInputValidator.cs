using FluentValidation;
using Recuro.Requisition.Domain.Requisitions;

namespace Recuro.Requisition.Application.Requisitions;

/// <summary>Shape checks for any save. Completeness for submit is the aggregate's rule (RCU-REQ-001).</summary>
internal sealed class RequisitionInputValidator : AbstractValidator<RequisitionInput>
{
    public RequisitionInputValidator()
    {
        RuleFor(i => i.Department).MaximumLength(RequisitionLimits.ShortText);
        RuleFor(i => i.Designation).MaximumLength(RequisitionLimits.ShortText);
        RuleFor(i => i.Grade).MaximumLength(RequisitionLimits.ShortText);
        RuleFor(i => i.Location).MaximumLength(RequisitionLimits.ShortText);
        RuleFor(i => i.ReportingManager).MaximumLength(RequisitionLimits.ShortText);
        RuleFor(i => i.Band).MaximumLength(RequisitionLimits.ShortText);
        RuleFor(i => i.ReplacementReason).MaximumLength(RequisitionLimits.LongText);
        RuleFor(i => i.OobJustification).MaximumLength(RequisitionLimits.LongText);
        RuleFor(i => i.Qualifications).MaximumLength(RequisitionLimits.LongText);
        RuleFor(i => i.Positions).InclusiveBetween(0, RequisitionLimits.MaxPositions);
        RuleFor(i => i.EmploymentType).IsInEnum();
        RuleFor(i => i.Nature).IsInEnum();
        RuleFor(i => i.JoiningDate)
            .Must(date => string.IsNullOrEmpty(date) || RequisitionInput.ParseDate(date) is not null)
            .WithErrorCode("date")
            .WithMessage("joiningDate must be a date (yyyy-MM-dd).");
        RuleFor(i => i.SourcingChannels)
            .Must(channels => channels is null || channels.Count <= RequisitionLimits.MaxSourcingChannels)
            .WithMessage($"At most {RequisitionLimits.MaxSourcingChannels} sourcing channels.");
        RuleForEach(i => i.SourcingChannels).MaximumLength(RequisitionLimits.ShortText);
    }
}
