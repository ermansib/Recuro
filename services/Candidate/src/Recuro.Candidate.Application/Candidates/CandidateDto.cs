using System.Globalization;
using System.Text.Json.Serialization;
using Recuro.Candidate.Domain.Candidates;
using CandidateEntity = Recuro.Candidate.Domain.Candidates.Candidate;

namespace Recuro.Candidate.Application.Candidates;

/// <summary>The frontend's <c>Consent</c>.</summary>
public sealed record ConsentDto(string Type, string TextVersion, string At);

/// <summary>
/// The frontend's <c>Candidate</c> (frontend/src/domain/types.ts), field for field. <c>sourceRef</c> is
/// left out when empty, like the mock. Masked fields come back as <c>null</c> or partially hidden.
/// </summary>
public sealed record CandidateDto(
    string Id,
    string Name,
    string Email,
    string Phone,
    decimal ExperienceYears,
    string Summary,
    decimal? CurrentCtc,
    decimal? ExpectedCtc,
    int? NoticeDays,
    string Source,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourceRef,
    IReadOnlyList<ConsentDto> Consents)
{
    public static CandidateDto From(CandidateEntity candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        return new CandidateDto(
            candidate.Id.ToString(),
            candidate.Name,
            candidate.Email,
            candidate.Phone ?? string.Empty,
            candidate.ExperienceYears,
            candidate.Summary,
            candidate.CurrentCtc,
            candidate.ExpectedCtc,
            candidate.NoticeDays,
            CandidateSourceNames.ToName(candidate.Attribution.Source),
            candidate.Attribution.SourceRef,
            candidate.Consents.Select(c => new ConsentDto(c.Type.ToString(), c.TextVersion, Iso(c.At))).ToList());
    }

    internal static string Iso(DateTimeOffset value) => value.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}

/// <summary>CV metadata, without the file itself.</summary>
public sealed record ResumeDto(string FileName, string ContentType, long SizeBytes, string UploadedAt)
{
    public static ResumeDto From(ResumeFile resume)
    {
        ArgumentNullException.ThrowIfNull(resume);
        return new ResumeDto(resume.FileName, resume.ContentType, resume.SizeBytes, CandidateDto.Iso(resume.UploadedAt));
    }
}
