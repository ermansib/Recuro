namespace Recuro.Config.Application;

/// <summary>Sizes and ranges, shared by validators and the EF configuration.</summary>
public static class ConfigLimits
{
    public const int NoteLength = 1000;
    public const int ReasonMinLength = 10;
    public const int ReasonLength = 2000;
    public const int ActorLength = 200;
    public const int ContentBytes = 256 * 1024;
    public const int MaxWorkingDays = 1000;

    /// <summary>Persona role keys (frontend <c>Role</c>): approver roles must be one of these.</summary>
    public static readonly IReadOnlyList<string> PersonaRoles = ["hrta", "hrhead", "mdceo", "employee", "candidate"];
}
