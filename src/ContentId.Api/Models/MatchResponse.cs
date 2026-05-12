namespace ContentId.Api.Models;

public sealed record MatchResponse(
    Guid SubmissionId,
    string Status,
    IReadOnlyCollection<MatchResult> Matches);
