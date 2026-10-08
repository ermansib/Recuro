using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Web.Http;
using Recuro.Vendor.Application.Vendors;
using Recuro.Vendor.Domain.Vendors;

namespace Recuro.Vendor.Api.Endpoints;

/// <summary>Vendor and consultant management API (S-14, RCU-VEN-001..004 / RCU-VND-001..003).</summary>
internal static class VendorEndpoints
{
    public static IEndpointRouteBuilder MapVendorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/vendors").WithTags("Vendors");

        group.MapGet("/", ListAsync)
            .RequireAuthorization(VendorPolicies.Read)
            .WithSummary("The S-14 partner list, by name. ?type=Consultant|BgvAgency&status=Pending|Active|Off.");

        group.MapPost("/", RegisterAsync)
            .RequireAuthorization(VendorPolicies.Manage)
            .WithSummary("RCU-VEN-001: register a partner (Pending) with its §15 evidence. Out-of-band fees need a justification (VND-002).");

        group.MapGet("/{vendorId}", GetAsync)
            .RequireAuthorization(VendorPolicies.Read)
            .WithSummary("One vendor.");

        group.MapGet("/{vendorId}/status", StatusAsync)
            .RequireAuthorization(VendorPolicies.Status)
            .WithSummary("{ vendorId, status: active|pending|off, active, name, type }. Unknown ids are 404 (treated as not active).");

        group.MapPut("/{vendorId}/empanelment", UpdateEmpanelmentAsync)
            .RequireAuthorization(VendorPolicies.Manage)
            .WithSummary("RCU-VEN-001: update gates, documents and fee terms while the vendor is pending.");

        group.MapPost("/{vendorId}/empanel", EmpanelAsync)
            .RequireAuthorization(VendorPolicies.Manage)
            .WithSummary("RCU-VEN-001 / VND-001: HR Head approval. 400 empanelment_gates_pending lists the unmet gates and missing documents.");

        group.MapPost("/{vendorId}/de-empanel", DeEmpanelAsync)
            .RequireAuthorization(VendorPolicies.Manage)
            .WithSummary("RCU-VEN-004 / VND-003: switch off with a reason; Bgv queues the vendor's open cases for reassignment.");

        return app;
    }

    private static async Task<IResult> ListAsync(string? type, string? status, IQueryHandler<ListVendorsQuery, IReadOnlyList<VendorDto>> handler, CancellationToken ct) =>
        (await handler.Handle(new ListVendorsQuery(type, status), ct)).ToHttpResult();

    private static async Task<IResult> RegisterAsync(RegisterVendorRequest request, ICommandHandler<RegisterVendorCommand, VendorDto> handler, CancellationToken ct)
    {
        var command = new RegisterVendorCommand(request.Name ?? string.Empty, request.Type ?? string.Empty, request.Fee, request.Gates, request.Documents);
        return (await handler.Handle(command, ct)).ToCreatedResult(dto => $"/api/v1/vendors/{dto.Id}");
    }

    private static async Task<IResult> GetAsync(string vendorId, IQueryHandler<GetVendorQuery, VendorDto> handler, CancellationToken ct) =>
        Guid.TryParse(vendorId, out var id)
            ? (await handler.Handle(new GetVendorQuery(id), ct)).ToHttpResult()
            : VendorErrors.NotFound(vendorId).ToProblem();

    private static async Task<IResult> StatusAsync(string vendorId, IQueryHandler<GetVendorStatusQuery, VendorStatusDto> handler, CancellationToken ct) =>
        Guid.TryParse(vendorId, out var id)
            ? (await handler.Handle(new GetVendorStatusQuery(id), ct)).ToHttpResult()
            : VendorErrors.NotFound(vendorId).ToProblem();

    private static async Task<IResult> UpdateEmpanelmentAsync(
        string vendorId,
        EmpanelmentRequest request,
        ICommandHandler<UpdateEmpanelmentCommand, VendorDto> handler,
        CancellationToken ct) =>
        Guid.TryParse(vendorId, out var id)
            ? (await handler.Handle(new UpdateEmpanelmentCommand(id, request.Fee, request.Gates, request.Documents), ct)).ToHttpResult()
            : VendorErrors.NotFound(vendorId).ToProblem();

    private static async Task<IResult> EmpanelAsync(string vendorId, ICommandHandler<EmpanelVendorCommand, VendorDto> handler, CancellationToken ct) =>
        Guid.TryParse(vendorId, out var id)
            ? (await handler.Handle(new EmpanelVendorCommand(id), ct)).ToHttpResult()
            : VendorErrors.NotFound(vendorId).ToProblem();

    private static async Task<IResult> DeEmpanelAsync(string vendorId, DeEmpanelRequest request, ICommandHandler<DeEmpanelVendorCommand, VendorDto> handler, CancellationToken ct) =>
        Guid.TryParse(vendorId, out var id)
            ? (await handler.Handle(new DeEmpanelVendorCommand(id, request.Reason ?? string.Empty), ct)).ToHttpResult()
            : VendorErrors.NotFound(vendorId).ToProblem();
}

/// <summary>Body of <c>POST /api/v1/vendors</c>.</summary>
internal sealed record RegisterVendorRequest(string? Name, string? Type, FeeInput? Fee, GatesInput? Gates, DocumentsInput? Documents);

/// <summary>Body of <c>PUT /api/v1/vendors/{vendorId}/empanelment</c>.</summary>
internal sealed record EmpanelmentRequest(FeeInput? Fee, GatesInput? Gates, DocumentsInput? Documents);

/// <summary>Body of <c>POST /api/v1/vendors/{vendorId}/de-empanel</c>.</summary>
internal sealed record DeEmpanelRequest(string? Reason);
