namespace ContentId.Api;

public sealed class ContentIdOptions
{
    public const string SectionName = "ContentId";

    public string SqlServerConnectionString { get; init; } =
        "Server=localhost,11433;Database=contentid;User Id=sa;Password=ContentId_local_2026!;Encrypt=True;TrustServerCertificate=True";

    public string MongoConnectionString { get; init; } = "mongodb://localhost:27017";

    public string MongoDatabase { get; init; } = "contentid";

    public string QueueName { get; init; } = "content-id-job-queue";

    public string AwsServiceUrl { get; init; } = "http://localhost:4566";

    public string AwsRegion { get; init; } = "us-east-1";

    public string ReferenceAssetsPath { get; init; } = "data/reference-assets/reference-assets.json";
}
