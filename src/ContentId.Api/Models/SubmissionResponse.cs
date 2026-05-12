namespace ContentId.Api.Models;

public sealed record SubmissionResponse(
    Guid SubmissionId,
    string Title,
    string SourcePlatform,
    string ContentType,
    int DurationSeconds,
    string FingerprintHash,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
