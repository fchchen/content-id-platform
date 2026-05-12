using ContentId.Api.Models;

namespace ContentId.Api.Services;

public interface IFingerprintDocumentStore
{
    Task StoreSubmissionDocumentAsync(SubmissionResponse submission, CancellationToken cancellationToken);
}
