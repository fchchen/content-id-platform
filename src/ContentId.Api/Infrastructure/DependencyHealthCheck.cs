using ContentId.Api.Services;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ContentId.Api.Infrastructure;

public sealed class DependencyHealthCheck(
    ISubmissionRepository submissions,
    IReferenceAssetCatalog referenceAssets)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await submissions.GetAsync(Guid.Empty, cancellationToken);
            var assets = await referenceAssets.GetReferenceAssetsAsync(cancellationToken);
            if (assets.Count == 0)
            {
                return HealthCheckResult.Unhealthy("Reference asset catalog is empty.");
            }

            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message, ex);
        }
    }
}
