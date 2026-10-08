using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Web.Http;
using Recuro.Careers.Application.Applications;
using Recuro.Careers.Application.Postings;

namespace Recuro.Careers.Api.Endpoints;

/// <summary>
/// Careers site API (RCU-CAR-001..007). The public group is anonymous: the tenant comes from the route
/// (the tenant's careers site knows its own id), and the gateway rate-limits it per IP (RCU-CAR-003).
/// </summary>
internal static class CareersEndpoints
{
    /// <summary>Where the public routes keep the client's Idempotency-Key (see <see cref="PublicIdempotency"/>).</summary>
    public const string IdempotencyKeyItem = "careers.idempotency-key";

    public static IEndpointRouteBuilder MapCareersEndpoints(this IEndpointRouteBuilder app)
    {
        var publicSite = app.MapGroup("/api/v1/careers/public/{tenantId}")
            .WithTags("Careers (public)")
            .AllowAnonymous()
            .AddEndpointFilter<PublicTenantFilter>();

        publicSite.MapGet("/jobs", SearchAsync)
            .WithSummary("RCU-CAR-001: published, open postings (frontend JobPosting), filtered by q, location and industry, cursor-paged, cached 60 s.");

        publicSite.MapPost("/applications", ApplyAsync)
            .WithSummary("RCU-CAR-002/004: apply with both consents (400 when missing). Runs the intake saga and returns the APP-ID (frontend PublicApplicationResult).");

        publicSite.MapGet("/applications/{appId}/status", StatusAsync)
            .WithSummary("RCU-CAR-005: coarse status for an APP-ID. Unknown ids get the same neutral 404.");

        var staff = app.MapGroup("/api/v1/careers/postings").WithTags("Careers (HR)");

        staff.MapGet("/", ListAsync)
            .RequireAuthorization(CareersPolicies.ReadPostings)
            .WithSummary("Every posting with its status, visibility and log.");

        staff.MapPut("/{reqId}", UpsertAsync)
            .RequireAuthorization(CareersPolicies.ManagePostings)
            .WithSummary("RCU-CAR-007: write or edit the advert for a requisition (a new one is a draft).");

        staff.MapPost("/{reqId}/publish", PublishAsync)
            .RequireAuthorization(CareersPolicies.ManagePostings)
            .WithSummary("RCU-CAR-007 / RCU-EMP-001: publish once sourcing is unlocked; visible after the IJP window unless the HR Head opens it early with a justification.");

        staff.MapPost("/{reqId}/unpublish", UnpublishAsync)
            .RequireAuthorization(CareersPolicies.ManagePostings)
            .WithSummary("RCU-CAR-007: take a posting down, with a logged reason.");

        return app;
    }

    private static async Task<IResult> SearchAsync(
        string? q,
        string? location,
        string? industry,
        string? cursor,
        int? limit,
        HttpResponse response,
        IQueryHandler<SearchJobsQuery, JobSearchPageDto> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new SearchJobsQuery(q, location, industry, cursor, limit), ct);
        if (result.IsSuccess)
        {
            response.Headers.CacheControl = "public, max-age=60";
        }

        return result.ToHttpResult();
    }

    private static async Task<IResult> ApplyAsync(
        PublicApplicationRequest request,
        HttpContext http,
        ICommandHandler<SubmitPublicApplicationCommand, PublicApplicationResultDto> handler,
        CancellationToken ct)
    {
        var command = new SubmitPublicApplicationCommand(
            request.PostingId ?? string.Empty,
            request.Name ?? string.Empty,
            request.Email ?? string.Empty,
            request.Phone ?? string.Empty,
            request.ExperienceYears,
            request.CurrentCtc,
            request.ExpectedCtc,
            request.NoticeDays,
            request.ResumeFileName ?? string.Empty,
            request.PrivacyConsent,
            request.CoiDeclaration,
            http.Items[IdempotencyKeyItem] as string);
        var result = await handler.Handle(command, ct);
        return result.ToCreatedResult(dto => $"/api/v1/careers/public/{http.GetRouteValue("tenantId")}/applications/{dto.AppId}/status");
    }

    private static async Task<IResult> StatusAsync(string appId, IQueryHandler<GetApplicationStatusQuery, ApplicationStatusDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GetApplicationStatusQuery(appId), ct)).ToHttpResult();

    private static async Task<IResult> ListAsync(IQueryHandler<ListPostingsQuery, IReadOnlyList<PostingAdminDto>> handler, CancellationToken ct) =>
        (await handler.Handle(new ListPostingsQuery(), ct)).ToHttpResult();

    private static async Task<IResult> UpsertAsync(
        string reqId,
        PostingRequest request,
        ICommandHandler<UpsertPostingCommand, PostingAdminDto> handler,
        CancellationToken ct)
    {
        var content = new PostingContentInput(
            request.Title ?? string.Empty,
            request.Location ?? string.Empty,
            request.LocationFilter ?? string.Empty,
            request.Experience ?? string.Empty,
            request.Qualification ?? string.Empty,
            request.Industry,
            request.Tags ?? []);
        return (await handler.Handle(new UpsertPostingCommand(reqId, content), ct)).ToHttpResult();
    }

    private static async Task<IResult> PublishAsync(
        string reqId,
        PublishRequest? request,
        ICommandHandler<PublishPostingCommand, PostingAdminDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new PublishPostingCommand(reqId, request?.OpenBeforeIjpWindowEnds ?? false, request?.Justification), ct)).ToHttpResult();

    private static async Task<IResult> UnpublishAsync(
        string reqId,
        UnpublishRequest request,
        ICommandHandler<UnpublishPostingCommand, PostingAdminDto> handler,
        CancellationToken ct) =>
        (await handler.Handle(new UnpublishPostingCommand(reqId, request.Reason ?? string.Empty), ct)).ToHttpResult();
}

/// <summary>
/// The public group's tenant comes from the route. An id that is not a tenant id gets the same neutral
/// 404 as an unknown resource, so the route can't be used to probe tenants.
/// </summary>
internal sealed class PublicTenantFilter(ScopeContext scope) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        if (!Guid.TryParse(context.HttpContext.GetRouteValue("tenantId") as string, out var tenantId) || tenantId == Guid.Empty)
        {
            return ApplicationErrors.StatusNotFound.ToProblem();
        }

        scope.SetTenant(tenantId);
        return await next(context);
    }
}

/// <summary>
/// The shared idempotency middleware keys replays by tenant and user, which an anonymous applicant
/// doesn't have, so two applicants could share a key's stored response. On the public routes the key is
/// taken off the request before that middleware runs and handled by the apply use case instead, scoped
/// to the applicant's email.
/// </summary>
internal static class PublicIdempotency
{
    public static IApplicationBuilder UsePublicIdempotency(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/api/v1/careers/public")
                && context.Request.Headers.Remove(BuildingBlocks.Web.Middleware.IdempotencyMiddleware.KeyHeader, out var key))
            {
                var value = key.ToString();
                if (value.Length is > 0 and <= 100)
                {
                    context.Items[CareersEndpoints.IdempotencyKeyItem] = value;
                }
            }

            await next(context);
        });
}

/// <summary>Body of the public apply, the frontend's <c>PublicApplicationInput</c>.</summary>
internal sealed record PublicApplicationRequest(
    string? PostingId,
    string? Name,
    string? Email,
    string? Phone,
    decimal? ExperienceYears,
    decimal? CurrentCtc,
    decimal? ExpectedCtc,
    int? NoticeDays,
    string? ResumeFileName,
    bool PrivacyConsent,
    bool CoiDeclaration);

/// <summary>Body of <c>PUT /api/v1/careers/postings/{reqId}</c>: the public card's fields plus an optional industry facet.</summary>
internal sealed record PostingRequest(
    string? Title,
    string? Location,
    string? LocationFilter,
    string? Experience,
    string? Qualification,
    string? Industry,
    IReadOnlyList<PostingTagDto>? Tags);

/// <summary>Body of <c>POST /api/v1/careers/postings/{reqId}/publish</c>.</summary>
internal sealed record PublishRequest(bool OpenBeforeIjpWindowEnds, string? Justification);

/// <summary>Body of <c>POST /api/v1/careers/postings/{reqId}/unpublish</c>.</summary>
internal sealed record UnpublishRequest(string? Reason);
