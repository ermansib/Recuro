namespace Recuro.Audit.Application.Entries;

/// <summary>Column sizes, shared by validators and the EF configuration.</summary>
public static class AuditLimits
{
    public const int ActorLength = 200;
    public const int RoleLength = 50;
    public const int EntityLength = 200;
    public const int ActionLength = 200;
    public const int StateLength = 4000;
    public const int ReasonLength = 2000;
    public const int ConfigVersionLength = 100;
    public const int SourceLength = 200;
    public const int DefaultPageSize = 100;
    public const int MaxPageSize = 500;
}
