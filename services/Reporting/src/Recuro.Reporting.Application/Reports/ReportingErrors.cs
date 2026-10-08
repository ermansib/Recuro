using Recuro.BuildingBlocks.Domain;

namespace Recuro.Reporting.Application.Reports;

public static class ReportingErrors
{
    public static readonly Error InvalidPeriod = Error.Validation([new FieldError("period", "invalid_period", "Use yyyy-MM for a month or yyyy-Qn for a quarter.")]);

    public static readonly Error PackNotFound = Error.NotFound("pack_not_found", "No such report pack for this tenant.");

    public static readonly Error PackForbidden = Error.Forbidden("pack_forbidden", "This pack is addressed to another role.");

    public static readonly Error FuturePeriod = Error.Validation([new FieldError("period", "period_not_started", "The period has not started yet.")]);
}

/// <summary>Column sizes and paging limits, shared by validators and the EF configuration.</summary>
public static class ReportingLimits
{
    public const int IdLength = 64;
    public const int TypeLength = 200;
    public const int SubjectLength = 300;
    public const int SourceLength = 50;
    public const int NoteLength = 500;
    public const int ActorLength = 200;
    public const int RoleLength = 50;
    public const int TimeZoneLength = 64;
    public const int CurrencyLength = 3;
    public const int HashLength = 64;
    public const int ReasonLength = 500;
    public const int ReplayBatch = 500;
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    public const int DefaultFunnelDays = 90;
    public const int MaxFunnelDays = 730;
    public const decimal MaxAmount = 1_000_000_000m;
}
