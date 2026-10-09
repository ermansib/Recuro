using System.Text.RegularExpressions;
using Recuro.Admin.Domain.Common;
using Recuro.Admin.Domain.Theming;

namespace Recuro.Admin.Domain.Tenants;

/// <summary>A customer of Recuro: an in-house HR team or a staffing agency. Managed from the platform console.</summary>
public sealed partial class Tenant : Entity
{
    public const int NameMaxLength = 120;
    public const int SlugMaxLength = 40;

    private Tenant()
    {
    }

    public string Name { get; private set; } = string.Empty;

    /// <summary>URL-safe key, also the default subdomain (for example acme.recuro.app).</summary>
    public string Slug { get; private set; } = string.Empty;

    public TenantKind Kind { get; private set; }

    public TenantPlan Plan { get; private set; }

    public TenantStatus Status { get; private set; }

    /// <summary>Optional custom domain the tenant's careers site and portal are served on.</summary>
    public string? CustomDomain { get; private set; }

    public string ThemePresetKey { get; private set; } = ThemePreset.DefaultKey;

    public ThemeMode ThemeMode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>What kind of organisation signed up. Only picks the starting settings below.</summary>
    public OrgType OrgType { get; private set; }

    /// <summary>Company email domain, taken from the owner's address at sign-up.</summary>
    public string? EmailDomain { get; private set; }

    public string CareersTagline { get; private set; } = string.Empty;

    /// <summary>Enterprise sign-in options shown on the login page (RCU-PLT-001), as frontend keys.</summary>
    public IReadOnlyList<string> SsoProviders { get; private set; } = [];

    /// <summary>Role keys that must pass a second factor at sign-in (RCU-PLT-001).</summary>
    public IReadOnlyList<string> MfaRoles { get; private set; } = [];

    public string Locale { get; private set; } = WorkspaceDefaults.Locale;

    public string Currency { get; private set; } = WorkspaceDefaults.Currency;

    /// <summary>Idle minutes before a session ends (NFR-01).</summary>
    public int SessionIdleMinutes { get; private set; } = WorkspaceDefaults.SessionIdleMinutes;

    public const int EmailDomainMaxLength = 253;

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$")]
    private static partial Regex SlugPattern();

    public static Result<Tenant> Create(
        string name, string slug, TenantKind kind, TenantPlan plan, DateTimeOffset now, Guid? id = null)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        var normalisedSlug = slug?.Trim().ToLowerInvariant() ?? string.Empty;

        if (trimmedName.Length is 0 or > NameMaxLength)
        {
            return TenantErrors.InvalidName;
        }

        if (normalisedSlug.Length is 0 or > SlugMaxLength || !SlugPattern().IsMatch(normalisedSlug))
        {
            return TenantErrors.InvalidSlug;
        }

        var tenant = new Tenant
        {
            Id = id ?? Guid.NewGuid(),
            Name = trimmedName,
            Slug = normalisedSlug,
            Kind = kind,
            Plan = plan,
            Status = TenantStatus.Active,
            ThemeMode = ThemeMode.System,
            CreatedAt = now,
        };
        tenant.ApplyOrgTypeDefaults(kind == TenantKind.Agency ? OrgType.Agency : OrgType.SmallBusiness);
        return tenant;
    }

    /// <summary>
    /// A workspace an organisation creates for itself on the sign-up page (RCU-PLT-001). It starts on the
    /// Starter plan with the org type's default settings; the platform console can change any of it later.
    /// </summary>
    public static Result<Tenant> SignUp(
        string name, string slug, OrgType orgType, string? emailDomain, string locale, string currency, DateTimeOffset now)
    {
        var kind = orgType == OrgType.Agency ? TenantKind.Agency : TenantKind.InHouse;
        var created = Create(name, slug, kind, TenantPlan.Starter, now);
        if (created.IsFailure)
        {
            return created;
        }

        var tenant = created.Value;
        tenant.ApplyOrgTypeDefaults(orgType);
        var domain = emailDomain?.Trim().ToLowerInvariant();
        tenant.EmailDomain = string.IsNullOrEmpty(domain) || domain.Length > EmailDomainMaxLength ? null : domain;
        tenant.Locale = string.IsNullOrWhiteSpace(locale) ? WorkspaceDefaults.Locale : locale.Trim();
        tenant.Currency = string.IsNullOrWhiteSpace(currency) ? WorkspaceDefaults.Currency : currency.Trim().ToUpperInvariant();
        return tenant;
    }

    private void ApplyOrgTypeDefaults(OrgType orgType)
    {
        var defaults = OrgTypeDefaults.For(orgType);
        OrgType = orgType;
        CareersTagline = defaults.CareersTagline;
        SsoProviders = defaults.SsoProviders;
        MfaRoles = defaults.MfaRoles;
    }

    public Result Rename(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (trimmed.Length is 0 or > NameMaxLength)
        {
            return TenantErrors.InvalidName;
        }

        Name = trimmed;
        return Result.Success();
    }

    public void ChangePlan(TenantPlan plan) => Plan = plan;

    public Result SetCustomDomain(string? domain)
    {
        var trimmed = string.IsNullOrWhiteSpace(domain) ? null : domain.Trim().ToLowerInvariant();
        if (trimmed is not null && Uri.CheckHostName(trimmed) != UriHostNameType.Dns)
        {
            return TenantErrors.InvalidDomain;
        }

        CustomDomain = trimmed;
        return Result.Success();
    }

    public Result Suspend()
    {
        if (Status == TenantStatus.Suspended)
        {
            return TenantErrors.AlreadyInStatus(Status);
        }

        Status = TenantStatus.Suspended;
        return Result.Success();
    }

    public Result Activate()
    {
        if (Status == TenantStatus.Active)
        {
            return TenantErrors.AlreadyInStatus(Status);
        }

        Status = TenantStatus.Active;
        return Result.Success();
    }

    /// <summary>Applies a published theme preset and the default colour mode (RCU-PLT-006).</summary>
    public Result ApplyTheme(ThemePreset preset, ThemeMode mode)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (!preset.IsPublished)
        {
            return ThemeErrors.NotPublished(preset.Key);
        }

        ThemePresetKey = preset.Key;
        ThemeMode = mode;
        return Result.Success();
    }
}

public enum TenantKind
{
    InHouse,
    Agency,
}

public enum TenantPlan
{
    Starter,
    Professional,
    Enterprise,
}

public enum TenantStatus
{
    Active,
    Suspended,
}

public static class TenantErrors
{
    public static readonly Error InvalidName =
        Error.Validation("tenant.name", $"Name is required and must be at most {Tenant.NameMaxLength} characters.");

    public static readonly Error InvalidSlug =
        Error.Validation("tenant.slug", "Slug must be lowercase letters, digits and hyphens, and must not start or end with a hyphen.");

    public static readonly Error InvalidDomain =
        Error.Validation("tenant.customDomain", "Custom domain must be a valid host name, for example careers.example.com.");

    public static readonly Error SlugTaken =
        Error.Conflict("tenant.slugTaken", "Another tenant already uses this slug.");

    public static Error NotFound(Guid id) => Error.NotFound("tenant.notFound", $"Tenant '{id}' was not found.");

    public static Error NotFound(string slug) => Error.NotFound("tenant.notFound", $"Tenant '{slug}' was not found.");

    public static Error AlreadyInStatus(TenantStatus status) =>
        Error.Conflict("tenant.status", $"Tenant is already {status.ToString().ToLowerInvariant()}.");
}
