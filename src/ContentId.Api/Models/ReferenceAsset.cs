namespace ContentId.Api.Models;

public sealed record ReferenceAsset(
    string ReferenceAssetId,
    string Title,
    string Owner,
    string FingerprintHash,
    string RightsPolicy);
