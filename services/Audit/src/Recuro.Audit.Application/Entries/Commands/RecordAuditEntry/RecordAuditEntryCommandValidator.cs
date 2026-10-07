using FluentValidation;

namespace Recuro.Audit.Application.Entries.Commands.RecordAuditEntry;

internal sealed class RecordAuditEntryCommandValidator : AbstractValidator<RecordAuditEntryCommand>
{
    public RecordAuditEntryCommandValidator(TimeProvider clock)
    {
        RuleFor(c => c.Entity).NotEmpty().MaximumLength(AuditLimits.EntityLength);
        RuleFor(c => c.Action).NotEmpty().MaximumLength(AuditLimits.ActionLength);
        RuleFor(c => c.Before).MaximumLength(AuditLimits.StateLength);
        RuleFor(c => c.After).MaximumLength(AuditLimits.StateLength);
        RuleFor(c => c.Reason).MaximumLength(AuditLimits.ReasonLength);
        RuleFor(c => c.ConfigVersion).MaximumLength(AuditLimits.ConfigVersionLength);
        RuleFor(c => c.ActorId).MaximumLength(AuditLimits.ActorLength);
        RuleFor(c => c.ActorName).MaximumLength(AuditLimits.ActorLength);
        RuleFor(c => c.ActorRole).MaximumLength(AuditLimits.RoleLength);
        RuleFor(c => c.OccurredAt)
            .Must(at => at is null || at <= clock.GetUtcNow().AddMinutes(5))
            .WithMessage("occurredAt cannot be in the future.");
    }
}
