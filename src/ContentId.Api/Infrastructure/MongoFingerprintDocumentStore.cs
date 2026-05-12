using ContentId.Api.Models;
using ContentId.Api.Services;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;

namespace ContentId.Api.Infrastructure;

public sealed class MongoFingerprintDocumentStore : IFingerprintDocumentStore
{
    private readonly IMongoCollection<BsonDocument> _collection;

    public MongoFingerprintDocumentStore(IOptions<ContentIdOptions> options)
    {
        var settings = options.Value;
        var client = new MongoClient(settings.MongoConnectionString);
        _collection = client.GetDatabase(settings.MongoDatabase)
                           .GetCollection<BsonDocument>("fingerprint_documents");
    }

    public async Task StoreSubmissionDocumentAsync(SubmissionResponse submission, CancellationToken cancellationToken)
    {
        var document = new BsonDocument
        {
            ["submissionId"] = submission.SubmissionId.ToString(),
            ["title"] = submission.Title,
            ["sourcePlatform"] = submission.SourcePlatform,
            ["contentType"] = submission.ContentType,
            ["durationSeconds"] = submission.DurationSeconds,
            ["fingerprintHash"] = submission.FingerprintHash,
            ["status"] = submission.Status,
            ["createdAt"] = submission.CreatedAt.UtcDateTime
        };

        await _collection.InsertOneAsync(document, cancellationToken: cancellationToken);
    }
}
