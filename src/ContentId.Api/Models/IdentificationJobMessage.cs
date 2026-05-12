namespace ContentId.Api.Models;

public sealed record IdentificationJobMessage(Guid SubmissionId, string FingerprintHash, DateTimeOffset EnqueuedAt);
