using ContentId.Api.Models;

namespace ContentId.Api.Services;

public interface IIdentificationQueue
{
    Task EnqueueAsync(IdentificationJobMessage message, CancellationToken cancellationToken);
}
