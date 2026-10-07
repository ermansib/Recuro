using Recuro.Candidate.Domain.Candidates;
using CandidateEntity = Recuro.Candidate.Domain.Candidates.Candidate;

namespace Recuro.Candidate.Application.Abstractions;

/// <summary>Candidates of the current tenant. Never returns another tenant's rows.</summary>
public interface ICandidateRepository
{
    Task<CandidateEntity?> GetAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<CandidateEntity>> GetManyAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct);

    /// <summary>An existing, unpurged candidate with the same email or phone fingerprint.</summary>
    Task<CandidateEntity?> FindByFingerprintAsync(ContactFingerprints fingerprints, CancellationToken ct);

    /// <summary>Candidates whose retention period ended before <paramref name="today"/>, oldest first.</summary>
    Task<IReadOnlyList<CandidateEntity>> ListDueForPurgeAsync(DateOnly today, int limit, CancellationToken ct);

    void Add(CandidateEntity candidate);
}

/// <summary>Keyed hashes of normalised contact details, so duplicates are found without decrypting.</summary>
public interface IContactFingerprinter
{
    ContactFingerprints Compute(string email, string? phone);
}

/// <summary>CV storage (local disk today, MinIO/S3 later). Content is encrypted before it is stored.</summary>
public interface IResumeStore
{
    Task SaveAsync(string key, Stream content, CancellationToken ct);

    Task<Stream?> OpenAsync(string key, CancellationToken ct);

    Task DeleteAsync(string key, CancellationToken ct);
}

/// <summary>RCU-CND-005: a consultant must be an active empanelled vendor (Vendor service, wave 2).</summary>
public interface IVendorDirectory
{
    Task<bool> IsActiveConsultantAsync(string consultantId, CancellationToken ct);
}

/// <summary>RCU-CND-004: every read of personal data is recorded, with identifiers only.</summary>
public interface IPersonalDataAccessLog
{
    void Read(IReadOnlyCollection<Guid> candidateIds, string purpose);
}

/// <summary>
/// RCU-AUT-004: the Identity service's masking map for one role on <c>candidate</c>. Null when Identity
/// has no map for the role (404 <c>masking_map_not_found</c>), which callers treat as "hide everything".
/// </summary>
public interface IMaskingMaps
{
    Task<IReadOnlyDictionary<string, Candidates.Masking.MaskStrategy>?> GetAsync(string role, CancellationToken ct);
}

/// <summary>The stable one-way hash for the <c>hash</c> masking strategy. Keyed, so it can't be reversed by guessing.</summary>
public interface IMaskHasher
{
    string Hash(string field, string value);
}
