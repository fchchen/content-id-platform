using System.Text.Json;
using ContentId.Api.Models;
using Microsoft.Extensions.Options;

namespace ContentId.Api.Services;

public sealed class JsonReferenceAssetCatalog(IOptions<ContentIdOptions> options, IWebHostEnvironment environment)
    : IReferenceAssetCatalog
{
    public async Task<IReadOnlyCollection<ReferenceAsset>> GetReferenceAssetsAsync(CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var path = Path.IsPathRooted(settings.ReferenceAssetsPath)
            ? settings.ReferenceAssetsPath
            : Path.Combine(environment.ContentRootPath, settings.ReferenceAssetsPath);

        if (!File.Exists(path))
        {
            path = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", settings.ReferenceAssetsPath));
        }

        if (!File.Exists(path))
        {
            return [];
        }

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<List<ReferenceAsset>>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cancellationToken) ?? [];
    }
}
