using ContentId.Api.Models;

namespace ContentId.Api.Services;

public interface IReferenceAssetCatalog
{
    Task<IReadOnlyCollection<ReferenceAsset>> GetReferenceAssetsAsync(CancellationToken cancellationToken);
}
