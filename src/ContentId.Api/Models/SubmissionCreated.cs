namespace ContentId.Api.Models;

public sealed record SubmissionCreated(SubmissionResponse? Submission, Dictionary<string, string[]> Errors)
{
    public bool IsValid => Errors.Count == 0;
}
