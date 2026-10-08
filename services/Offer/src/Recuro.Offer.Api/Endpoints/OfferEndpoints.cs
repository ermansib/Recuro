using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.BuildingBlocks.Web.Http;
using Recuro.Offer.Api.Http;
using Recuro.Offer.Application.Abstractions;
using Recuro.Offer.Application.Dashboard;
using Recuro.Offer.Application.Letters;
using Recuro.Offer.Application.Offers;
using Recuro.Offer.Application.Offers.Commands;
using Recuro.Offer.Application.Offers.Queries;
using Recuro.Offer.Domain.Offers;
using Recuro.Offer.Infrastructure.Adapters;

namespace Recuro.Offer.Api.Endpoints;

/// <summary>Offer management API (S-12). Shapes are the frontend's <c>Offer</c> (frontend/src/domain/types.ts).</summary>
internal static class OfferEndpoints
{
    private const string SignatureHeader = "X-Recuro-Signature";

    public static IEndpointRouteBuilder MapOfferEndpoints(this IEndpointRouteBuilder app)
    {
        var offers = app.MapGroup("/api/v1/offers").WithTags("Offers");

        offers.MapGet("/", ListAsync)
            .RequireAuthorization(OfferPolicies.Read)
            .WithSummary("The frontend's listOffers, newest first, masked for the caller (?state=, ?appId=).");

        offers.MapGet("/dashboard/ta", TaDashboardAsync)
            .RequireAuthorization(OfferPolicies.Read)
            .WithSummary("RCU-DSH-001: the Offers Pending tile for the gateway's TA dashboard (/bff/dashboard/ta).");

        offers.MapGet("/{id:guid}", GetAsync)
            .RequireAuthorization(OfferPolicies.Read)
            .WithSummary("One offer. The ETag is the version to send back in If-Match.");

        offers.MapPost("/", CreateAsync)
            .RequireAuthorization(OfferPolicies.Edit)
            .WithSummary("RCU-OFR-001: draft an offer for a selected application. The CTC must pass the tenant's Finance rule-set.");

        offers.MapPut("/{id:guid}/components", ReviseAsync)
            .RequireAuthorization(OfferPolicies.Edit)
            .WithSummary("RCU-OFR-001/005 (frontend updateOfferComponents): revise the CTC; the trail records it. After submission it needs a new approval.");

        offers.MapPost("/{id:guid}/verbal", LogVerbalAsync)
            .RequireAuthorization(OfferPolicies.Edit)
            .WithSummary("RCU-OFR-005: log a verbal offer {value, outcome}; appended to the trail.");

        offers.MapPost("/{id:guid}/submit", SubmitAsync)
            .RequireAuthorization(OfferPolicies.Edit)
            .WithSummary("RCU-OFR-002/003 (frontend submitOffer): resolve the Annexure D route and open the approval task.");

        offers.MapPost("/{id:guid}/approve", ApproveAsync)
            .RequireAuthorization(OfferPolicies.Approve)
            .WithSummary("RCU-OFR-003 (frontend approveOffer): decide the same task the inbox shows; approve by default, or {actionId: decline, reason}.");

        offers.MapPost("/{id:guid}/send", SendAsync)
            .RequireAuthorization(OfferPolicies.Release)
            .WithSummary("RCU-OFR-004/007 (frontend sendOffer): release with e-sign. 409 listing blockers until BGV clears; HR Head may send a conditional offer.");

        offers.MapPost("/{id:guid}/outcome", OutcomeAsync)
            .RequireAuthorization(OfferPolicies.Edit)
            .WithSummary("RCU-OFR-006 (frontend setOfferOutcome): record Accepted or Declined. Accepted starts pre-boarding.");

        offers.MapPost("/{id:guid}/withdraw", WithdrawAsync)
            .RequireAuthorization(OfferPolicies.Withdraw)
            .WithSummary("RCU-OFR-006: withdraw with a documented reason (HR Head, or MD/CEO).");

        offers.MapGet("/{id:guid}/letter", LetterAsync)
            .RequireAuthorization(OfferPolicies.Read)
            .WithSummary("RCU-OFR-004: the latest letter PDF, or ?signed=true for the countersigned copy.");

        offers.MapPost("/{id:guid}/letter-link", LetterLinkAsync)
            .RequireAuthorization(OfferPolicies.Edit)
            .WithSummary("RCU-OFR-004: a presigned link to the current letter, valid for 7 days.");

        offers.MapGet("/letters/{tenantId:guid}/{id:guid}/{version:int}", LetterByLinkAsync)
            .AllowAnonymous()
            .WithSummary("RCU-OFR-004: opens a presigned letter link. The signature is the credential; it expires.");

        offers.MapPost("/esign/callback", ESignCallbackAsync)
            .AllowAnonymous()
            .WithSummary("RCU-OFR-004: the e-sign provider's callback, signed with X-Recuro-Signature (hex HMAC-SHA256 of the body).");

        return app;
    }

    private static async Task<IResult> ListAsync(
        OfferState? state,
        string? appId,
        int? limit,
        IQueryHandler<ListOffersQuery, IReadOnlyList<OfferDto>> handler,
        CancellationToken ct) =>
        (await handler.Handle(new ListOffersQuery(state, appId, limit ?? ListOffersQuery.DefaultLimit), ct)).ToHttpResult();

    private static async Task<IResult> TaDashboardAsync(IQueryHandler<GetTaDashboardQuery, DashboardFragmentDto> handler, CancellationToken ct) =>
        (await handler.Handle(new GetTaDashboardQuery(), ct)).ToHttpResult();

    private static async Task<IResult> GetAsync(Guid id, HttpContext http, IQueryHandler<GetOfferQuery, VersionedOffer> handler, CancellationToken ct) =>
        Versioned(http, await handler.Handle(new GetOfferQuery(id), ct));

    private static async Task<IResult> CreateAsync(
        CreateOfferRequest request,
        HttpContext http,
        ICommandHandler<CreateOfferCommand, VersionedOffer> handler,
        CancellationToken ct)
    {
        var result = await handler.Handle(new CreateOfferCommand(request), ct);
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        ETags.Write(http, result.Value.Version);
        return Results.Created($"/api/v1/offers/{result.Value.Offer.Id}", result.Value.Offer);
    }

    private static async Task<IResult> ReviseAsync(
        Guid id,
        ReviseComponentsRequest request,
        HttpContext http,
        ICommandHandler<ReviseComponentsCommand, VersionedOffer> handler,
        CancellationToken ct) =>
        Versioned(http, await handler.Handle(new ReviseComponentsCommand(id, ETags.ReadIfMatch(http), request), ct));

    private static async Task<IResult> LogVerbalAsync(
        Guid id,
        VerbalOfferRequest request,
        HttpContext http,
        ICommandHandler<LogVerbalOfferCommand, VersionedOffer> handler,
        CancellationToken ct) =>
        Versioned(http, await handler.Handle(new LogVerbalOfferCommand(id, request), ct));

    private static async Task<IResult> SubmitAsync(
        Guid id,
        HttpContext http,
        ICommandHandler<SubmitOfferCommand, VersionedOffer> handler,
        CancellationToken ct) =>
        Versioned(http, await handler.Handle(new SubmitOfferCommand(id, ETags.ReadIfMatch(http)), ct));

    private static async Task<IResult> ApproveAsync(
        Guid id,
        [FromBody] DecideOfferRequest? request,
        HttpContext http,
        ICommandHandler<DecideOfferCommand, VersionedOffer> handler,
        CancellationToken ct) =>
        Versioned(http, await handler.Handle(new DecideOfferCommand(id, request ?? new DecideOfferRequest(null, null)), ct));

    private static async Task<IResult> SendAsync(
        Guid id,
        [FromBody] SendOfferRequest? request,
        HttpContext http,
        ICommandHandler<SendOfferCommand, VersionedOffer> handler,
        CancellationToken ct) =>
        Versioned(http, await handler.Handle(new SendOfferCommand(id, ETags.ReadIfMatch(http), request ?? new SendOfferRequest(null)), ct));

    private static async Task<IResult> OutcomeAsync(
        Guid id,
        OfferOutcomeRequest request,
        HttpContext http,
        ICommandHandler<RecordOfferOutcomeCommand, VersionedOffer> handler,
        CancellationToken ct) =>
        Versioned(http, await handler.Handle(new RecordOfferOutcomeCommand(id, request), ct));

    private static async Task<IResult> WithdrawAsync(
        Guid id,
        WithdrawOfferRequest request,
        HttpContext http,
        ICommandHandler<WithdrawOfferCommand, VersionedOffer> handler,
        CancellationToken ct) =>
        Versioned(http, await handler.Handle(new WithdrawOfferCommand(id, request), ct));

    private static async Task<IResult> LetterAsync(
        Guid id,
        bool? signed,
        IQueryHandler<GetOfferLetterQuery, OfferFile> handler,
        CancellationToken ct) =>
        File(await handler.Handle(new GetOfferLetterQuery(id, signed == true), ct));

    private static async Task<IResult> LetterLinkAsync(Guid id, ICommandHandler<CreateLetterLinkCommand, LetterLink> handler, CancellationToken ct) =>
        (await handler.Handle(new CreateLetterLinkCommand(id), ct)).ToHttpResult();

    private static async Task<IResult> LetterByLinkAsync(
        Guid tenantId,
        Guid id,
        int version,
        long? exp,
        string? sig,
        ILetterLinks links,
        ScopeContext scope,
        TimeProvider clock,
        IQueryHandler<GetLetterByLinkQuery, OfferFile> handler,
        CancellationToken ct)
    {
        if (exp is null || sig is null || !links.Verify(tenantId, id, version, exp.Value, sig, clock.GetUtcNow()))
        {
            return Error.NotFound("letter_not_found", "This link is invalid or has expired.").ToProblem();
        }

        scope.SetTenant(tenantId);
        scope.SetUser("system:letter-link", "Letter link", []);
        return File(await handler.Handle(new GetLetterByLinkQuery(id, version), ct));
    }

    private static async Task<IResult> ESignCallbackAsync(
        HttpContext http,
        ESignCallbackVerifier verifier,
        ScopeContext scope,
        ICommandHandler<ApplyESignCallbackCommand> handler,
        CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        await http.Request.Body.CopyToAsync(buffer, ct);
        var body = buffer.ToArray();
        if (!verifier.Verify(body, http.Request.Headers[SignatureHeader].ToString()))
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "The callback signature is missing or wrong.");
        }

        ESignCallback? callback;
        try
        {
            callback = JsonSerializer.Deserialize<ESignCallback>(body, http.RequestServices.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions);
        }
        catch (JsonException)
        {
            callback = null;
        }

        if (callback is null || callback.TenantId == Guid.Empty)
        {
            return Error.Validation("callback_invalid", "The callback body could not be read.").ToProblem();
        }

        scope.SetTenant(callback.TenantId);
        scope.SetUser("system:esign", "E-sign provider", []);
        return (await handler.Handle(new ApplyESignCallbackCommand(callback), ct)).ToHttpResult();
    }

    private static IResult File(Result<OfferFile> result) =>
        result.IsFailure ? result.Error!.ToProblem() : Results.File(result.Value.Content, result.Value.ContentType, result.Value.FileName);

    private static IResult Versioned(HttpContext http, Result<VersionedOffer> result)
    {
        if (result.IsFailure)
        {
            return result.Error!.ToProblem();
        }

        ETags.Write(http, result.Value.Version);
        return Results.Ok(result.Value.Offer);
    }
}
