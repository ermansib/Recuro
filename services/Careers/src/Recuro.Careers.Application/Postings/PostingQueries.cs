using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FluentValidation;
using Microsoft.Extensions.Options;
using Recuro.BuildingBlocks.Application.Abstractions;
using Recuro.BuildingBlocks.Application.Messaging;
using Recuro.BuildingBlocks.Domain;
using Recuro.Careers.Application.Abstractions;

namespace Recuro.Careers.Application.Postings;

/// <summary>
/// RCU-CAR-001: public search over published, open postings, keyset-paged and cached for 60 seconds.
/// Payloads and cursors carry no personal data.
/// </summary>
public sealed record SearchJobsQuery(string? Q, string? Location, string? Industry, string? Cursor, int? Limit) : IQuery<JobSearchPageDto>;

internal sealed class SearchJobsQueryValidator : AbstractValidator<SearchJobsQuery>
{
    public SearchJobsQueryValidator()
    {
        RuleFor(q => q.Q).MaximumLength(CareersLimits.QueryLength);
        RuleFor(q => q.Location).MaximumLength(CareersLimits.LocationLength);
        RuleFor(q => q.Industry).MaximumLength(CareersLimits.ShortTextLength);
        RuleFor(q => q.Limit).InclusiveBetween(1, 100);
    }
}

internal sealed class SearchJobsQueryHandler(
    IPostingRepository postings,
    IJobSearchCache cache,
    ITenantContext tenant,
    IOptions<CareersOptions> options,
    TimeProvider clock) : IQueryHandler<SearchJobsQuery, JobSearchPageDto>
{
    public async Task<Result<JobSearchPageDto>> Handle(SearchJobsQuery query, CancellationToken ct)
    {
        if (!JobCursor.TryDecode(query.Cursor, out var after))
        {
            return Error.Validation([new FieldError("cursor", "invalid_cursor", "The cursor is not valid. Start again without it.")]);
        }

        var settings = options.Value;
        var limit = Math.Min(query.Limit ?? settings.DefaultPageSize, settings.MaxPageSize);
        var cacheKey = CacheKey(tenant.RequiredTenantId, query, limit);
        if (await cache.GetAsync<JobSearchPageDto>(cacheKey, ct) is { } cached)
        {
            return cached;
        }

        // One extra row tells whether there is a next page.
        var search = new JobSearch(Clean(query.Q), Clean(query.Location), Clean(query.Industry), after, limit + 1);
        var rows = await postings.SearchVisibleAsync(search, clock.GetUtcNow(), ct);
        var page = rows.Take(limit).ToList();
        var next = rows.Count > limit ? JobCursor.Encode(new SearchCursor(page[^1].VisibleFrom!.Value, page[^1].PostingId)) : null;
        var result = new JobSearchPageDto(page.Select(JobPostingDto.From).ToList(), next);

        await cache.SetAsync(cacheKey, result, ct);
        return result;
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CacheKey(Guid tenantId, SearchJobsQuery query, int limit)
    {
        var raw = string.Join('\u001f', query.Q?.Trim().ToUpperInvariant(), query.Location?.Trim().ToUpperInvariant(), query.Industry?.Trim().ToUpperInvariant(), query.Cursor, limit);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
        return $"jobs:{tenantId:N}:{hash}";
    }
}

/// <summary>The opaque keyset cursor: base64url of <c>visibleFromTicks|postingId</c>.</summary>
public static class JobCursor
{
    public static string Encode(SearchCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        var raw = string.Create(CultureInfo.InvariantCulture, $"{cursor.VisibleFrom.UtcTicks}|{cursor.PostingId}");
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(raw));
    }

    public static bool TryDecode(string? value, out SearchCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrEmpty(value))
        {
            return true;
        }

        try
        {
            var raw = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(value));
            var parts = raw.Split('|', 2);
            if (parts.Length != 2 || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks < DateTimeOffset.MinValue.UtcTicks || ticks > DateTimeOffset.MaxValue.UtcTicks
                || parts[1].Length is 0 or > CareersLimits.PostingIdLength)
            {
                return false;
            }

            cursor = new SearchCursor(new DateTimeOffset(ticks, TimeSpan.Zero), parts[1]);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

/// <summary>RCU-CAR-007: every posting of the tenant with its status and log, for HR.</summary>
public sealed record ListPostingsQuery : IQuery<IReadOnlyList<PostingAdminDto>>;

internal sealed class ListPostingsQueryHandler(IPostingRepository postings) : IQueryHandler<ListPostingsQuery, IReadOnlyList<PostingAdminDto>>
{
    public async Task<Result<IReadOnlyList<PostingAdminDto>>> Handle(ListPostingsQuery query, CancellationToken ct) =>
        Result.Success<IReadOnlyList<PostingAdminDto>>((await postings.ListAsync(ct)).Select(PostingAdminDto.From).ToList());
}
