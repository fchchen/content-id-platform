using ContentId.Api.Models;

namespace ContentId.Api.Services;

public interface ISubmissionRepository
{
    Task CreateAsync(SubmissionResponse submission, CancellationToken cancellationToken);
    Task<SubmissionResponse?> GetAsync(Guid submissionId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<MatchResult>?> GetMatchesAsync(Guid submissionId, CancellationToken cancellationToken);
}
