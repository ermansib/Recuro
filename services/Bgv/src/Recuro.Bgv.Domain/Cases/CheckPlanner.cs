namespace Recuro.Bgv.Domain.Cases;

/// <summary>Grades of the DOA matrix, same strings as the frontend's <c>Grade</c>.</summary>
public static class Grades
{
    public const string E = "E";
    public const string M1 = "M1";
    public const string M3 = "M3";
    public const string Vp = "VP";
    public const string Kmp = "KMP";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal) { E, M1, M3, Vp, Kmp };

    /// <summary>Senior management and KMP: Fit &amp; Proper governance (FRD §5.5).</summary>
    public static bool IsSeniorManagement(string grade) => grade is Vp or Kmp;

    /// <summary>"Senior" for risk-based checks such as court records: M3 and above.</summary>
    public static bool IsSenior(string grade) => grade is M3 or Vp or Kmp;
}

/// <summary>Role-risk flags HR-TA sets at initiation (RCU-BGV-002), the inputs of FRD §5.5.</summary>
public static class RoleFlags
{
    public const string CashHandling = "cashHandling";
    public const string Field = "field";
    public const string CustomerFacing = "customerFacing";
    public const string SensitiveData = "sensitiveData";
    public const string Finance = "finance";
    public const string Fresher = "fresher";
    public const string ExtensiveTravel = "extensiveTravel";
    public const string SocialMediaCheck = "socialMediaCheck";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        CashHandling, Field, CustomerFacing, SensitiveData, Finance, Fresher, ExtensiveTravel, SocialMediaCheck,
    };
}

/// <summary>The role being verified: its grade, risk flags and whether the candidate is internal.</summary>
public sealed record RoleProfile(string Grade, IReadOnlySet<string> Flags, CheckScope Scope)
{
    public bool Has(string flag) => Flags.Contains(flag);
}

/// <summary>
/// An optional machine-readable condition on a Config BGV matrix row. When the row carries one it wins
/// over the built-in rule for that check type, so tenants change applicability by configuration.
/// </summary>
public sealed record AppliesWhen(bool Always, IReadOnlyList<string> Grades, IReadOnlyList<string> AnyFlags);

/// <summary>One row of the Config BGV matrix (frontend <c>BgvCheckRule</c>), plus its optional condition.</summary>
public sealed record CheckRule(string Type, string Label, string Detail, string Condition, AppliesWhen? AppliesWhen = null);

/// <summary>A check as planned for a case: applicable, or not applicable with the reason shown greyed out.</summary>
public sealed record PlannedCheck(string Type, string Label, string Detail, bool Applicable, string? NotApplicableReason);

/// <summary>
/// RCU-BGV-002: turns the Config BGV matrix (FRD §5.5) into the checks for one role. Rows without a
/// machine-readable <see cref="AppliesWhen"/> use the §5.5 rule for their type; unknown types always
/// apply, so a tenant's new check is never silently skipped.
/// </summary>
public static class CheckPlanner
{
    private static readonly HashSet<string> OnFileForInternal = new(StringComparer.Ordinal) { "identity", "education", "employment" };

    public static IReadOnlyList<PlannedCheck> Plan(IEnumerable<CheckRule> matrix, RoleProfile role)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(role);
        return matrix
            .Select(rule =>
            {
                var reason = NotApplicableReason(rule, role);
                return new PlannedCheck(rule.Type, rule.Label, rule.Detail, reason is null, reason);
            })
            .ToList();
    }

    /// <summary>Null when the check applies; otherwise why not.</summary>
    private static string? NotApplicableReason(CheckRule rule, RoleProfile role)
    {
        if (role.Scope == CheckScope.Delta && OnFileForInternal.Contains(rule.Type))
        {
            return "Not applicable — internal candidate, on file (delta-only scope)";
        }

        var applies = rule.AppliesWhen is { } when ? Matches(when, role) : BuiltIn(rule.Type, role);
        return applies ? null : $"Not applicable ({role.Grade}) — {rule.Condition}";
    }

    private static bool Matches(AppliesWhen when, RoleProfile role) =>
        when.Always
        || when.Grades.Contains(role.Grade, StringComparer.Ordinal)
        || when.AnyFlags.Any(role.Has);

    /// <summary>FRD §5.5, by check type.</summary>
    private static bool BuiltIn(string type, RoleProfile role) => type switch
    {
        "employment" => !role.Has(RoleFlags.Fresher),
        "police" => role.Has(RoleFlags.CashHandling) || role.Has(RoleFlags.Field) || role.Has(RoleFlags.CustomerFacing) || role.Has(RoleFlags.SensitiveData),
        "credit" => role.Has(RoleFlags.Finance) || Grades.IsSeniorManagement(role.Grade),
        "fitProper" => Grades.IsSeniorManagement(role.Grade),
        "court" => Grades.IsSenior(role.Grade) || role.Has(RoleFlags.CustomerFacing),
        "social" => Grades.IsSeniorManagement(role.Grade) && role.Has(RoleFlags.SocialMediaCheck),
        "medical" => role.Has(RoleFlags.ExtensiveTravel),

        // identity, education, coi and any check a tenant adds: always.
        _ => true,
    };
}
