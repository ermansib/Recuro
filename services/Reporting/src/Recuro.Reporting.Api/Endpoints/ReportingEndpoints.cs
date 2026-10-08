using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Web.Auth;
using Recuro.BuildingBlocks.Web.Http;
using Recuro.Reporting.Application.Reports;
using Recuro.Reporting.Application.Reports.Commands;
using Recuro.Reporting.Application.Reports.Queries;

namespace Recuro.Reporting.Api.Endpoints;

/// <summary>RBAC from FRD §3.2 "View KPI reports" (HR-TA team view, HR Head full, MD/CEO board pack).</summary>
internal static class ReportingPolicies
{
    /// <summary><c>reports.view</c>: HR-TA, HR Head and MD/CEO read reports, masked per role.</summary>
    public const string View = "reports.view";

    /// <summary>HR Head owns the register: definitions, spend, snapshots, packs, settings and replays.</summary>
    public const string Manage = "reports.manage";

    public static IServiceCollection AddReportingPolicies(this IServiceCollection services)
    {
        services.AddAuthorizationBuilder()
            .AddRolePolicy(View, RecuroRoles.HrStaff)
            .AddRolePolicy(Manage, RecuroRoles.HrHead);
        return services;
    }
}

/// <summary>Reports &amp; KPIs API (S-15, RCU-RPT-001..005). Everything is computed from Reporting's own read models.</summary>
internal static class ReportingEndpoints
{
    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder app)
    {
        var reports = app.MapGroup("/api/v1/reports").WithTags("Reports");

        reports.MapGet("/kpis", KpisAsync)
            .RequireAuthorization(ReportingPolicies.View)
            .WithSummary("RCU-RPT-001/002: the nine §12 metrics for ?period=yyyy-MM|yyyy-Qn (default: this month), with targets, status and definitions.");

        reports.MapGet("/source-mix", SourceMixAsync)
            .RequireAuthorization(ReportingPolicies.View)
            .WithSummary("RCU-RPT-003: share of hires and cost per hire by source channel; channel spend is masked for HR-TA.");

        reports.MapGet("/funnel", FunnelAsync)
            .RequireAuthorization(ReportingPolicies.View)
            .WithSummary("RCU-RPT-004: Sourced→Screened→Interviewed→BGV→Offered→Joined counts and conversion (?from&to, or ?days, default 90).");

        reports.MapGet("/dashboard/ta", TaDashboardAsync)
            .RequireAuthorization(ReportingPolicies.View)
            .WithSummary("RCU-DSH-001: the KPI card for the gateway's TA dashboard (/bff/dashboard/ta).");

        reports.MapGet("/definitions", DefinitionsAsync)
            .RequireAuthorization(ReportingPolicies.View)
            .WithSummary("RCU-RPT-002: the metric-definition registry in force, with its version history.");

        reports.MapPost("/definitions", PublishDefinitionsAsync)
            .RequireAuthorization(ReportingPolicies.Manage)
            .WithSummary("RCU-RPT-002: publish a new registry version {effectiveFrom?, definitions[]}; it never applies to the past.");

        reports.MapGet("/costs", CostsAsync)
            .RequireAuthorization(ReportingPolicies.Manage)
            .WithSummary("Recorded recruitment spend (?period=), the input to Cost per Hire.");

        reports.MapPost("/costs", RecordCostAsync)
            .RequireAuthorization(ReportingPolicies.Manage)
            .WithSummary("RCU-RPT-003: record spend {month: yyyy-MM, source, amount, currency, note?}; consultant fees as billed.");

        reports.MapGet("/snapshots", SnapshotsAsync)
            .RequireAuthorization(ReportingPolicies.Manage)
            .WithSummary("Frozen registers, newest first, with their SHA-256 hashes (?period=, ?limit=).");

        reports.MapPost("/snapshots/{period}/recompute", RecomputeAsync)
            .RequireAuthorization(ReportingPolicies.Manage)
            .WithSummary("RCU-RPT-005: freeze a new revision of a period's register {reason?}; the hash is stored and logged.");

        reports.MapGet("/packs", PacksAsync)
            .RequireAuthorization(ReportingPolicies.View)
            .WithSummary("RCU-RPT-003: archived packs; HR Head sees all, others only packs addressed to their role.");

        reports.MapPost("/packs", GeneratePackAsync)
            .RequireAuthorization(ReportingPolicies.Manage)
            .WithSummary("RCU-RPT-003: build and archive a pack {period, recipientRole?, send}; send hands it to Notification to email.");

        reports.MapGet("/packs/{id:guid}/{format:regex(^(pdf|csv)$)}", PackFileAsync)
            .RequireAuthorization(ReportingPolicies.View)
            .WithSummary("Download an archived pack as PDF or CSV.");

        reports.MapGet("/settings", SettingsAsync)
            .RequireAuthorization(ReportingPolicies.Manage)
            .WithSummary("The tenant's reporting time zone and pack schedule.");

        reports.MapPut("/settings", UpdateSettingsAsync)
            .RequireAuthorization(ReportingPolicies.Manage)
            .WithSummary("Set {timeZone, monthlyPack, monthlyRecipientRole, quarterlyPack, quarterlyRecipientRole}.");

        reports.MapGet("/projections", ProjectionStatusAsync)
            .RequireAuthorization(ReportingPolicies.Manage)
            .WithSummary("RCU-RPT-001: events in the log, newest offset and the projection checkpoint.");

        reports.MapPost("/projections/replay", ReplayAsync)
            .RequireAuthorization(ReportingPolicies.Manage)
            .WithSummary("RCU-RPT-001/005: re-fold projections from the log {fromSequence?, reset}; reset rebuilds from scratch.");

        return app;
    }

    private static async Task<IResult> KpisAsync(string? period, IQueryHandler<GetKpiRegisterQuery, KpiRegisterDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GetKpiRegisterQuery(period), ct)).ToHttpResult();

    private static async Task<IResult> SourceMixAsync(string? period, IQueryHandler<GetSourceMixQuery, SourceMixDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GetSourceMixQuery(period), ct)).ToHttpResult();

    private static async Task<IResult> FunnelAsync(DateTimeOffset? from, DateTimeOffset? to, int? days, IQueryHandler<GetFunnelQuery, FunnelDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GetFunnelQuery(from, to, days), ct)).ToHttpResult();

    private static async Task<IResult> TaDashboardAsync(IQueryHandler<GetTaDashboardQuery, DashboardFragmentDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GetTaDashboardQuery(), ct)).ToHttpResult();

    private static async Task<IResult> DefinitionsAsync(IQueryHandler<GetDefinitionsQuery, DefinitionsDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GetDefinitionsQuery(), ct)).ToHttpResult();

    private static async Task<IResult> PublishDefinitionsAsync(PublishDefinitionsRequest request, ICommandHandler<PublishDefinitionsCommand, DefinitionsDto> handler, CancellationToken ct) =>
        (await handler.Handle(new PublishDefinitionsCommand(request.EffectiveFrom, request.Definitions ?? []), ct))
            .ToCreatedResult(_ => "/api/v1/reports/definitions");

    private static async Task<IResult> CostsAsync(string? period, IQueryHandler<ListCostsQuery, IReadOnlyList<CostDto>> handler, CancellationToken ct) =>
        (await handler.Handle(new ListCostsQuery(period), ct)).ToHttpResult();

    private static async Task<IResult> RecordCostAsync(RecordCostRequest request, ICommandHandler<RecordCostCommand, CostDto> handler, CancellationToken ct) =>
        (await handler.Handle(new RecordCostCommand(request.Month ?? string.Empty, request.Source ?? string.Empty, request.Amount, request.Currency ?? string.Empty, request.Note), ct))
            .ToCreatedResult(dto => $"/api/v1/reports/costs?period={dto.Month}");

    private static async Task<IResult> SnapshotsAsync(string? period, int? limit, IQueryHandler<ListSnapshotsQuery, IReadOnlyList<SnapshotDto>> handler, CancellationToken ct) =>
        (await handler.Handle(new ListSnapshotsQuery(period, limit), ct)).ToHttpResult();

    private static async Task<IResult> RecomputeAsync(string period, RecomputeRequest? request, ICommandHandler<RecomputeSnapshotCommand, SnapshotDto> handler, CancellationToken ct) =>
        (await handler.Handle(new RecomputeSnapshotCommand(period, request?.Reason), ct))
            .ToCreatedResult(dto => $"/api/v1/reports/snapshots?period={dto.Period}");

    private static async Task<IResult> PacksAsync(int? limit, IQueryHandler<ListPacksQuery, IReadOnlyList<PackDto>> handler, CancellationToken ct) =>
        (await handler.Handle(new ListPacksQuery(limit), ct)).ToHttpResult();

    private static async Task<IResult> GeneratePackAsync(GeneratePackRequest request, ICommandHandler<GeneratePackCommand, PackDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GeneratePackCommand(request.Period ?? string.Empty, request.RecipientRole, request.Send), ct))
            .ToCreatedResult(dto => dto.PdfUrl);

    private static async Task<IResult> PackFileAsync(Guid id, string format, IQueryHandler<GetPackFileQuery, PackFile> handler, CancellationToken ct)
    {
        var result = await handler.Handle(new GetPackFileQuery(id, format), ct);
        return result.IsSuccess
            ? Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName)
            : result.Error!.ToProblem();
    }

    private static async Task<IResult> SettingsAsync(IQueryHandler<GetSettingsQuery, SettingsDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GetSettingsQuery(), ct)).ToHttpResult();

    private static async Task<IResult> UpdateSettingsAsync(UpdateSettingsRequest request, ICommandHandler<UpdateSettingsCommand, SettingsDto> handler, CancellationToken ct) =>
        (await handler.Handle(
            new UpdateSettingsCommand(
                request.TimeZone ?? string.Empty,
                request.MonthlyPack,
                request.MonthlyRecipientRole ?? string.Empty,
                request.QuarterlyPack,
                request.QuarterlyRecipientRole ?? string.Empty),
            ct)).ToHttpResult();

    private static async Task<IResult> ProjectionStatusAsync(IQueryHandler<GetProjectionStatusQuery, ProjectionStatusDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GetProjectionStatusQuery(), ct)).ToHttpResult();

    private static async Task<IResult> ReplayAsync(ReplayRequest? request, ICommandHandler<ReplayProjectionsCommand, ReplayResultDto> handler, CancellationToken ct) =>
        (await handler.Handle(new ReplayProjectionsCommand(request?.FromSequence, request?.Reset ?? false), ct)).ToHttpResult();
}

internal sealed record PublishDefinitionsRequest(DateTimeOffset? EffectiveFrom, IReadOnlyList<MetricDefinitionInput>? Definitions);

internal sealed record RecordCostRequest(string? Month, string? Source, decimal Amount, string? Currency, string? Note);

internal sealed record RecomputeRequest(string? Reason);

internal sealed record GeneratePackRequest(string? Period, string? RecipientRole, bool Send);

internal sealed record UpdateSettingsRequest(string? TimeZone, bool MonthlyPack, string? MonthlyRecipientRole, bool QuarterlyPack, string? QuarterlyRecipientRole);

internal sealed record ReplayRequest(long? FromSequence, bool Reset);
