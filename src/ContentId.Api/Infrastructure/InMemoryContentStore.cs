using System.Collections.Concurrent;
using ContentId.Api.Models;
using ContentId.Api.Services;

namespace ContentId.Api.Infrastructure;

public sealed class InMemoryContentStore : ISubmissionRepository, IFingerprintDocumentStore
{
    private readonly ConcurrentDictionary<Guid, SubmissionResponse> _submissions = [];
    private readonly ConcurrentDictionary<Guid, IReadOnlyCollection<MatchResult>> _matches = [];

    public Task CreateAsync(SubmissionResponse submission, CancellationToken cancellationToken)
    {
        _submissions[submission.SubmissionId] = submission;
        _matches[submission.SubmissionId] = [];
        return Task.CompletedTask;
    }

    public Task<SubmissionResponse?> GetAsync(Guid submissionId, CancellationToken cancellationToken)
    {
        _submissions.TryGetValue(submissionId, out var submission);
        return Task.FromResult(submission);
    }

    public Task<IReadOnlyCollection<MatchResult>?> GetMatchesAsync(Guid submissionId, CancellationToken cancellationToken)
    {
        _matches.TryGetValue(submissionId, out var matches);
        return Task.FromResult(matches);
    }

    public Task StoreSubmissionDocumentAsync(SubmissionResponse submission, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
