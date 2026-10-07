namespace Recuro.Candidate.Domain.Candidates;

/// <summary>The candidate's CV. The bytes live in the file store under <see cref="StorageKey"/>, encrypted.</summary>
public sealed record ResumeFile(string FileName, string ContentType, long SizeBytes, string StorageKey, DateTimeOffset UploadedAt);
