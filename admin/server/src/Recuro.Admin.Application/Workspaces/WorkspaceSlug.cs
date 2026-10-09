using System.Globalization;
using System.Text;
using Recuro.Admin.Domain.Tenants;

namespace Recuro.Admin.Application.Workspaces;

/// <summary>Same rules as <c>slugify</c> in frontend/src/domain/auth.ts: `Aurora Housing Finance` → `aurora-housing-finance`.</summary>
public static class WorkspaceSlug
{
    public const string Fallback = "workspace";

    public static string From(string name)
    {
        var builder = new StringBuilder();
        var pendingHyphen = false;
        foreach (var c in (name ?? string.Empty).Normalize(NormalizationForm.FormKD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue; // drop accents: Ü → U
            }

            var lower = char.ToLowerInvariant(c);
            if (char.IsAsciiLetterLower(lower) || char.IsAsciiDigit(lower))
            {
                if (pendingHyphen && builder.Length > 0)
                {
                    builder.Append('-');
                }

                builder.Append(lower);
                pendingHyphen = false;
            }
            else
            {
                pendingHyphen = true;
            }
        }

        var slug = builder.ToString();
        slug = slug[..Math.Min(slug.Length, Tenant.SlugMaxLength)].TrimEnd('-');
        return slug.Length == 0 ? Fallback : slug;
    }
}
