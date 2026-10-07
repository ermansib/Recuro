namespace Recuro.Identity.Application.Users;

/// <summary>Column sizes, shared by validators and the EF configuration.</summary>
public static class UserLimits
{
    public const int SubjectLength = 100;
    public const int NameLength = 200;
    public const int EmailLength = 320;
    public const int RoleLength = 50;
}
