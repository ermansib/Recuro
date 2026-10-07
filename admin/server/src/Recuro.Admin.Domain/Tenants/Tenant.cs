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

        return new Tenant
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
