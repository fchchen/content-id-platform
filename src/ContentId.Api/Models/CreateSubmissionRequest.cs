namespace ContentId.Api.Models;

public sealed record CreateSubmissionRequest(
    string Title,
    string SourcePlatform,
    string ContentType,
    int DurationSeconds,
    string FingerprintHash);
