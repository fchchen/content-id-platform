namespace ContentId.Api.Models;

public sealed record MatchResult(
    string ReferenceAssetId,
    string Title,
    string Owner,
    string RightsPolicy,
    double Confidence,
    string FingerprintHash);
