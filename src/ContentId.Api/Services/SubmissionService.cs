using System.Diagnostics;
using ContentId.Api.Models;

namespace ContentId.Api.Services;

public sealed class SubmissionService(
    ISubmissionRepository submissions,
    IFingerprintDocumentStore documents,
    IIdentificationQueue queue,
    ActivitySource activitySource)
{
    public async Task<SubmissionCreated> CreateSubmissionAsync(
        CreateSubmissionRequest request,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return new SubmissionCreated(null, errors);
        }

        using var activity = activitySource.StartActivity("submission.create");
        var now = DateTimeOffset.UtcNow;
        var submission = new SubmissionResponse(
            Guid.NewGuid(),
            request.Title.Trim(),
            request.SourcePlatform.Trim(),
            request.ContentType.Trim().ToLowerInvariant(),
            request.DurationSeconds,
            request.FingerprintHash.Trim().ToLowerInvariant(),
            "queued",
            now,
            now);

        activity?.SetTag("submission.id", submission.SubmissionId.ToString());
        activity?.SetTag("content.type", submission.ContentType);
        activity?.SetTag("source.platform", submission.SourcePlatform);

        await submissions.CreateAsync(submission, cancellationToken);
        await documents.StoreSubmissionDocumentAsync(submission, cancellationToken);
        await queue.EnqueueAsync(
            new IdentificationJobMessage(submission.SubmissionId, submission.FingerprintHash, now),
            cancellationToken);

        return new SubmissionCreated(submission, []);
    }

    public Task<SubmissionResponse?> GetSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) =>
        submissions.GetAsync(submissionId, cancellationToken);

    public async Task<MatchResponse?> GetMatchesAsync(Guid submissionId, CancellationToken cancellationToken)
    {
        var submission = await submissions.GetAsync(submissionId, cancellationToken);
        if (submission is null)
        {
            return null;
        }

        var matches = await submissions.GetMatchesAsync(submissionId, cancellationToken) ?? [];
        return new MatchResponse(submissionId, submission.Status, matches);
    }

    private static Dictionary<string, string[]> Validate(CreateSubmissionRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            errors[nameof(request.Title)] = ["Title is required."];
        }

        if (string.IsNullOrWhiteSpace(request.SourcePlatform))
        {
            errors[nameof(request.SourcePlatform)] = ["SourcePlatform is required."];
        }

        var contentType = request.ContentType?.Trim().ToLowerInvariant();
        if (contentType is not ("audio" or "video"))
        {
            errors[nameof(request.ContentType)] = ["ContentType must be audio or video."];
        }

        if (request.DurationSeconds <= 0)
        {
            errors[nameof(request.DurationSeconds)] = ["DurationSeconds must be positive."];
        }

        if (string.IsNullOrWhiteSpace(request.FingerprintHash))
        {
            errors[nameof(request.FingerprintHash)] = ["FingerprintHash is required."];
        }

        return errors;
    }
}
