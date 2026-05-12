using ContentId.Api.Models;
using ContentId.Api.Services;

namespace ContentId.Api.Infrastructure;

public sealed class NoopIdentificationQueue : IIdentificationQueue
{
    public Task EnqueueAsync(IdentificationJobMessage message, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
