using FluentValidation;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Vendor.Application.Abstractions;
using Recuro.Vendor.Domain.Vendors;
using VendorEntity = Recuro.Vendor.Domain.Vendors.Vendor;

namespace Recuro.Vendor.Application.Vendors;

/// <summary>The fee terms as sent by the client.</summary>
public sealed record FeeInput(string? Kind, decimal? Value, string? Justification, string? TermsVersion);

/// <summary>Gate evidence as sent by the client; missing gates count as unchecked.</summary>
public sealed record GatesInput(bool? Experience, bool? TrackRecord, bool? Agreement, bool? Nda, bool? ReplacementGuarantee, bool? PrivacyAck)
{
    public EmpanelmentGates ToGates() =>
        new(Experience ?? false, TrackRecord ?? false, Agreement ?? false, Nda ?? false, ReplacementGuarantee ?? false, PrivacyAck ?? false);
}

public sealed record DocumentsInput(string? NdaRef, string? AgreementRef, string? PrivacyAckRef)
{
    public VendorDocuments ToDocuments() => new(Clean(NdaRef), Clean(AgreementRef), Clean(PrivacyAckRef));

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <summary>Shared rules for fee terms (RCU-VND-002): out-of-band percentages need a justification.</summary>
internal static class FeeRules
{
    public const string DefaultTermsVersion = "v1";

    public static void Apply<T>(AbstractValidator<T> validator, Func<T, FeeInput?> fee, FeeBand band)
    {
        validator.RuleFor(c => fee(c)).NotNull().OverridePropertyName("fee");
        validator.RuleFor(c => fee(c)!.Kind).IsEnumName(typeof(FeeKind), caseSensitive: true)
            .WithMessage($"Fee kind must be one of: {string.Join(", ", Enum.GetNames<FeeKind>())}.")
            .OverridePropertyName("fee.kind").When(c => fee(c) is not null);
        validator.RuleFor(c => fee(c)!.Value).NotNull().GreaterThan(0).OverridePropertyName("fee.value").When(c => fee(c) is not null);
        validator.RuleFor(c => fee(c)!.Value).LessThanOrEqualTo(100).OverridePropertyName("fee.value")
            .When(c => fee(c) is { Kind: nameof(FeeKind.PercentOfCtc) });
        validator.RuleFor(c => fee(c)!.Justification).MaximumLength(VendorLimits.JustificationLength).OverridePropertyName("fee.justification").When(c => fee(c) is not null);
        validator.RuleFor(c => fee(c)!.TermsVersion).MaximumLength(VendorLimits.TermsVersionLength).OverridePropertyName("fee.termsVersion").When(c => fee(c) is not null);
        validator.RuleFor(c => fee(c)!.Justification)
            .Must(j => j is not null && j.Trim().Length >= VendorLimits.MinJustificationLength)
            .WithErrorCode("justification_required")
            .WithMessage($"The fee is outside the {band.MinPercent}–{band.MaxPercent}% band; a justification of at least {VendorLimits.MinJustificationLength} characters is mandatory.")
            .OverridePropertyName("fee.justification")
            .When(c => fee(c) is { Kind: nameof(FeeKind.PercentOfCtc), Value: { } v } && (v < band.MinPercent || v > band.MaxPercent));
    }

    public static FeeTerms ToTerms(FeeInput fee) =>
        new(
            Enum.Parse<FeeKind>(fee.Kind!),
            fee.Value!.Value,
            string.IsNullOrWhiteSpace(fee.Justification) ? null : fee.Justification.Trim(),
            string.IsNullOrWhiteSpace(fee.TermsVersion) ? DefaultTermsVersion : fee.TermsVersion.Trim());
}

// ---------- register ----------

/// <summary>RCU-VEN-001: HR Head registers a partner (Pending) with its §15 evidence so far.</summary>
public sealed record RegisterVendorCommand(string Name, string Type, FeeInput? Fee, GatesInput? Gates, DocumentsInput? Documents) : ICommand<VendorDto>;

internal sealed class RegisterVendorCommandValidator : AbstractValidator<RegisterVendorCommand>
{
    public RegisterVendorCommandValidator(IOptions<VendorOptions> options)
    {
        RuleFor(c => c.Name).NotEmpty().MaximumLength(VendorLimits.NameLength);
        RuleFor(c => c.Type).IsEnumName(typeof(VendorType), caseSensitive: true)
            .WithMessage($"Type must be one of: {string.Join(", ", Enum.GetNames<VendorType>())}.");
        FeeRules.Apply(this, c => c.Fee, options.Value.FeeBand);
        DocumentRules.Apply(this, c => c.Documents);
    }
}

internal static class DocumentRules
{
    public static void Apply<T>(AbstractValidator<T> validator, Func<T, DocumentsInput?> documents)
    {
        validator.RuleFor(c => documents(c)!.NdaRef).MaximumLength(VendorLimits.DocumentRefLength).OverridePropertyName("documents.ndaRef").When(c => documents(c) is not null);
        validator.RuleFor(c => documents(c)!.AgreementRef).MaximumLength(VendorLimits.DocumentRefLength).OverridePropertyName("documents.agreementRef").When(c => documents(c) is not null);
        validator.RuleFor(c => documents(c)!.PrivacyAckRef).MaximumLength(VendorLimits.DocumentRefLength).OverridePropertyName("documents.privacyAckRef").When(c => documents(c) is not null);
    }
}

internal sealed class RegisterVendorCommandHandler(
    IVendorRepository vendors,
    ICurrentUser caller,
    IUnitOfWork unitOfWork,
    IOptions<VendorOptions> options,
    TimeProvider clock) : ICommandHandler<RegisterVendorCommand, VendorDto>
{
    public async Task<Result<VendorDto>> Handle(RegisterVendorCommand command, CancellationToken ct)
    {
        var name = command.Name.Trim();
        if (await vendors.NameTakenAsync(name, ct))
        {
            return Error.Conflict("vendor_exists", $"A vendor named {name} is already registered.");
        }

        var vendor = VendorEntity.Register(
            name,
            Enum.Parse<VendorType>(command.Type),
            FeeRules.ToTerms(command.Fee!),
            (command.Gates ?? new GatesInput(null, null, null, null, null, null)).ToGates(),
            (command.Documents ?? new DocumentsInput(null, null, null)).ToDocuments(),
            Actors.From(caller),
            clock.GetUtcNow());
        vendors.Add(vendor);
        await unitOfWork.SaveChangesAsync(ct);
        return VendorDto.From(vendor, options.Value.FeeBand);
    }
}

// ---------- update evidence ----------

/// <summary>RCU-VEN-001: HR Head ticks gates, files documents or revises terms while the vendor is pending.</summary>
public sealed record UpdateEmpanelmentCommand(Guid VendorId, FeeInput? Fee, GatesInput? Gates, DocumentsInput? Documents) : ICommand<VendorDto>;

internal sealed class UpdateEmpanelmentCommandValidator : AbstractValidator<UpdateEmpanelmentCommand>
{
    public UpdateEmpanelmentCommandValidator(IOptions<VendorOptions> options)
    {
        FeeRules.Apply(this, c => c.Fee, options.Value.FeeBand);
        DocumentRules.Apply(this, c => c.Documents);
    }
}

internal sealed class UpdateEmpanelmentCommandHandler(
    IVendorRepository vendors,
    IUnitOfWork unitOfWork,
    IOptions<VendorOptions> options,
    TimeProvider clock) : ICommandHandler<UpdateEmpanelmentCommand, VendorDto>
{
    public async Task<Result<VendorDto>> Handle(UpdateEmpanelmentCommand command, CancellationToken ct)
    {
        var vendor = await vendors.GetAsync(command.VendorId, ct);
        if (vendor is null)
        {
            return VendorErrors.NotFound(command.VendorId.ToString());
        }

        var updated = vendor.UpdateEmpanelment(
            (command.Gates ?? new GatesInput(null, null, null, null, null, null)).ToGates(),
            (command.Documents ?? new DocumentsInput(null, null, null)).ToDocuments(),
            FeeRules.ToTerms(command.Fee!),
            clock.GetUtcNow());
        if (updated.IsFailure)
        {
            return updated.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return VendorDto.From(vendor, options.Value.FeeBand);
    }
}

// ---------- empanel / de-empanel ----------

/// <summary>RCU-VEN-001 / VND-001: HR Head approves; Active only with all six gates and the documents.</summary>
public sealed record EmpanelVendorCommand(Guid VendorId) : ICommand<VendorDto>;

internal sealed class EmpanelVendorCommandHandler(
    IVendorRepository vendors,
    ICurrentUser caller,
    IUnitOfWork unitOfWork,
    IOptions<VendorOptions> options,
    TimeProvider clock) : ICommandHandler<EmpanelVendorCommand, VendorDto>
{
    public async Task<Result<VendorDto>> Handle(EmpanelVendorCommand command, CancellationToken ct)
    {
        var vendor = await vendors.GetAsync(command.VendorId, ct);
        if (vendor is null)
        {
            return VendorErrors.NotFound(command.VendorId.ToString());
        }

        var band = options.Value.FeeBand;
        var empanelled = vendor.Empanel(band, Actors.From(caller), clock.GetUtcNow());
        if (empanelled.IsFailure)
        {
            return empanelled.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return VendorDto.From(vendor, band);
    }
}

/// <summary>RCU-VEN-004 / VND-003: HR Head switches a vendor off; Bgv queues its open cases for reassignment.</summary>
public sealed record DeEmpanelVendorCommand(Guid VendorId, string Reason) : ICommand<VendorDto>;

internal sealed class DeEmpanelVendorCommandValidator : AbstractValidator<DeEmpanelVendorCommand>
{
    public DeEmpanelVendorCommandValidator()
    {
        RuleFor(c => c.Reason).NotEmpty().WithErrorCode("reason_required").MaximumLength(VendorLimits.ReasonLength);
    }
}

internal sealed class DeEmpanelVendorCommandHandler(
    IVendorRepository vendors,
    ICurrentUser caller,
    IUnitOfWork unitOfWork,
    IOptions<VendorOptions> options,
    TimeProvider clock) : ICommandHandler<DeEmpanelVendorCommand, VendorDto>
{
    public async Task<Result<VendorDto>> Handle(DeEmpanelVendorCommand command, CancellationToken ct)
    {
        var vendor = await vendors.GetAsync(command.VendorId, ct);
        if (vendor is null)
        {
            return VendorErrors.NotFound(command.VendorId.ToString());
        }

        var result = vendor.DeEmpanel(command.Reason, Actors.From(caller), clock.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error!;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return VendorDto.From(vendor, options.Value.FeeBand);
    }
}

// ---------- queries ----------

public sealed record ListVendorsQuery(string? Type, string? Status) : IQuery<IReadOnlyList<VendorDto>>;

internal sealed class ListVendorsQueryValidator : AbstractValidator<ListVendorsQuery>
{
    public ListVendorsQueryValidator()
    {
        RuleFor(q => q.Type).IsEnumName(typeof(VendorType), caseSensitive: true).When(q => q.Type is not null);
        RuleFor(q => q.Status).IsEnumName(typeof(VendorStatus), caseSensitive: true).When(q => q.Status is not null);
    }
}

internal sealed class ListVendorsQueryHandler(IVendorRepository vendors, IOptions<VendorOptions> options)
    : IQueryHandler<ListVendorsQuery, IReadOnlyList<VendorDto>>
{
    public async Task<Result<IReadOnlyList<VendorDto>>> Handle(ListVendorsQuery query, CancellationToken ct)
    {
        var band = options.Value.FeeBand;
        var list = await vendors.ListAsync(
            query.Type is null ? null : Enum.Parse<VendorType>(query.Type),
            query.Status is null ? null : Enum.Parse<VendorStatus>(query.Status),
            ct);
        return list.Select(v => VendorDto.From(v, band)).ToList();
    }
}

public sealed record GetVendorQuery(Guid VendorId) : IQuery<VendorDto>;

internal sealed class GetVendorQueryHandler(IVendorRepository vendors, IOptions<VendorOptions> options) : IQueryHandler<GetVendorQuery, VendorDto>
{
    public async Task<Result<VendorDto>> Handle(GetVendorQuery query, CancellationToken ct)
    {
        var vendor = await vendors.GetAsync(query.VendorId, ct);
        return vendor is null ? VendorErrors.NotFound(query.VendorId.ToString()) : VendorDto.From(vendor, options.Value.FeeBand);
    }
}

/// <summary>The recorded status contract used by Candidate (consultant check) and Bgv (agency check).</summary>
public sealed record GetVendorStatusQuery(Guid VendorId) : IQuery<VendorStatusDto>;

internal sealed class GetVendorStatusQueryHandler(IVendorRepository vendors) : IQueryHandler<GetVendorStatusQuery, VendorStatusDto>
{
    public async Task<Result<VendorStatusDto>> Handle(GetVendorStatusQuery query, CancellationToken ct)
    {
        var vendor = await vendors.GetAsync(query.VendorId, ct);
        return vendor is null ? VendorErrors.NotFound(query.VendorId.ToString()) : VendorStatusDto.From(vendor);
    }
}
