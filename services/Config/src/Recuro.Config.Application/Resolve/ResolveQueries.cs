using System.Text.Json;
using FluentValidation;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Config.Application.RuleSets;
using Recuro.Config.Domain.Rules;
using Recuro.Config.Domain.RuleSets;

namespace Recuro.Config.Application.Resolve;

/// <summary>
/// RCU-CFG-002: <c>GET /api/v1/resolve/doa?grade=M3&amp;budget=in|oob&amp;at=…[&amp;versionId=…]</c>, the contract in
/// services/docs/architecture.md ("Synchronous contracts").
/// </summary>
public sealed record ResolveDoaQuery(string? Grade, string? Budget, DateTimeOffset? At, Guid? VersionId) : IQuery<ResolvedDoaDto>;

/// <summary>The frontend <c>DoaRoute</c> plus the version it came from and the workflow legs.</summary>
public sealed record ResolvedDoaDto(
    Guid ConfigVersionId,
    string Grade,
    string BudgetStatus,
    string Initiating,
    string Recommending,
    string Approving,
    string ApproverRole,
    string BandLabel,
    TatRange OverallTat,
    IReadOnlyList<ApprovalLeg> Legs);

/// <summary><c>GET /api/v1/resolve/working-days?from=…&amp;days=…[&amp;location=…][&amp;versionId=…]</c>; negative days count backwards.</summary>
public sealed record ResolveWorkingDaysQuery(DateOnly? From, int? Days, string? Location, Guid? VersionId) : IQuery<WorkingDaysDto>;

public sealed record WorkingDaysDto(DateOnly Date, Guid ConfigVersionId);

/// <summary>Any matrix as a whole, for consumers such as Offer (offer matrix) or Bgv (check matrix).</summary>
public sealed record ResolveMatrixQuery(MatrixType MatrixType, DateTimeOffset? At, Guid? VersionId) : IQuery<ResolvedMatrixDto>;

public sealed record ResolvedMatrixDto(Guid ConfigVersionId, string MatrixType, int Number, DateTimeOffset EffectiveFrom, JsonElement Content);

internal sealed class ResolveDoaQueryValidator : AbstractValidator<ResolveDoaQuery>
{
    public ResolveDoaQueryValidator()
    {
        RuleFor(q => q.Grade).NotEmpty().MaximumLength(20);
        RuleFor(q => q.Budget)
            .Must(b => b is null || Enum.TryParse<BudgetStatus>(b, ignoreCase: true, out _) && !int.TryParse(b, out _))
            .WithMessage("budget must be 'in' or 'oob'.");
    }
}

internal sealed class ResolveWorkingDaysQueryValidator : AbstractValidator<ResolveWorkingDaysQuery>
{
    public ResolveWorkingDaysQueryValidator()
    {
        RuleFor(q => q.From).NotNull();
        RuleFor(q => q.Days).NotNull().InclusiveBetween(-ConfigLimits.MaxWorkingDays, ConfigLimits.MaxWorkingDays);
        RuleFor(q => q.Location).MaximumLength(100);
    }
}

internal sealed class ResolveDoaQueryHandler(RuleSetResolver resolver) : IQueryHandler<ResolveDoaQuery, ResolvedDoaDto>
{
    public async Task<Result<ResolvedDoaDto>> Handle(ResolveDoaQuery query, CancellationToken ct)
    {
        var version = await resolver.ResolveAsync(MatrixType.Doa, query.At, query.VersionId, ct);
        if (version.IsFailure)
        {
            return version.Error!;
        }

        var matrix = MatrixJson.Read<DoaMatrix>(version.Value.Content);
        var route = matrix.Find(query.Grade!);
        if (route is null)
        {
            return Error.NotFound(
                "unknown_grade",
                $"No DOA route for grade '{query.Grade}'. Known grades: {string.Join(", ", matrix.Routes.Select(r => r.Grade))}.");
        }

        var budget = query.Budget is null ? BudgetStatus.In : Enum.Parse<BudgetStatus>(query.Budget, ignoreCase: true);
        return new ResolvedDoaDto(
            version.Value.Id,
            route.Grade,
            budget.ToString().ToLowerInvariant(),
            route.Initiating,
            route.Recommending,
            route.Approving,
            route.ApproverRole,
            route.BandLabel,
            route.OverallTat,
            DoaMatrix.LegsFor(route, budget));
    }
}

internal sealed class ResolveWorkingDaysQueryHandler(RuleSetResolver resolver) : IQueryHandler<ResolveWorkingDaysQuery, WorkingDaysDto>
{
    public async Task<Result<WorkingDaysDto>> Handle(ResolveWorkingDaysQuery query, CancellationToken ct)
    {
        var version = await resolver.ResolveAsync(MatrixType.Calendar, null, query.VersionId, ct);
        if (version.IsFailure)
        {
            return version.Error!;
        }

        var matrix = MatrixJson.Read<CalendarMatrix>(version.Value.Content);
        var calendar = matrix.Find(query.Location);
        if (calendar is null)
        {
            return Error.NotFound(
                "unknown_location",
                $"No business calendar for location '{query.Location}'. Known locations: {string.Join(", ", matrix.Calendars.Select(c => c.Location))}.");
        }

        return new WorkingDaysDto(calendar.AddWorkingDays(query.From!.Value, query.Days!.Value), version.Value.Id);
    }
}

internal sealed class ResolveMatrixQueryHandler(RuleSetResolver resolver) : IQueryHandler<ResolveMatrixQuery, ResolvedMatrixDto>
{
    public async Task<Result<ResolvedMatrixDto>> Handle(ResolveMatrixQuery query, CancellationToken ct)
    {
        var version = await resolver.ResolveAsync(query.MatrixType, query.At, query.VersionId, ct);
        if (version.IsFailure)
        {
            return version.Error!;
        }

        var v = version.Value;
        return new ResolvedMatrixDto(v.Id, v.MatrixType.ToKey(), v.Number, v.EffectiveFrom, MatrixJson.ToElement(v.Content));
    }
}
