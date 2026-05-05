using System.Diagnostics;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using ContentId.Api;
using ContentId.Api.Infrastructure;
using ContentId.Api.Models;
using ContentId.Api.Services;
using MongoDB.Bson;
using MongoDB.Driver;
using Npgsql;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ContentIdOptions>(builder.Configuration.GetSection(ContentIdOptions.SectionName));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSingleton(new ActivitySource("ContentId.Api"));

if (builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddSingleton<InMemoryContentStore>();
    builder.Services.AddSingleton<ISubmissionRepository>(sp => sp.GetRequiredService<InMemoryContentStore>());
    builder.Services.AddSingleton<IFingerprintDocumentStore>(sp => sp.GetRequiredService<InMemoryContentStore>());
    builder.Services.AddSingleton<IIdentificationQueue, NoopIdentificationQueue>();
    builder.Services.AddSingleton<IStorageInitializer, NoopStorageInitializer>();
}
else
{
    builder.Services.AddSingleton<ISubmissionRepository, PostgresSubmissionRepository>();
    builder.Services.AddSingleton<IFingerprintDocumentStore, MongoFingerprintDocumentStore>();
    builder.Services.AddSingleton<IIdentificationQueue, SqsIdentificationQueue>();
    builder.Services.AddSingleton<IStorageInitializer, PostgresStorageInitializer>();
}

builder.Services.AddSingleton<IReferenceAssetCatalog, JsonReferenceAssetCatalog>();
builder.Services.AddSingleton<SubmissionService>();
builder.Services.AddHealthChecks().AddCheck<DependencyHealthCheck>("content-id-dependencies");

builder.Services.AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService("content-id-api"))
    .WithTracing(tracing =>
    {
        tracing.AddSource("ContentId.Api");
        tracing.AddAspNetCoreInstrumentation();
        tracing.AddOtlpExporter();
    });

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<IStorageInitializer>().InitializeAsync();
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health");

app.MapGet("/metrics", () => Results.Text(
    """
    # HELP content_id_platform_info Static service metadata.
    # TYPE content_id_platform_info gauge
    content_id_platform_info{service="content-id-api"} 1
    """,
    "text/plain"));

app.MapPost("/v1/submissions", async (
    CreateSubmissionRequest request,
    SubmissionService service,
    CancellationToken cancellationToken) =>
{
    var result = await service.CreateSubmissionAsync(request, cancellationToken);
    return result.IsValid
        ? Results.Created($"/v1/submissions/{result.Submission!.SubmissionId}", result.Submission)
        : Results.ValidationProblem(result.Errors);
})
.WithName("CreateSubmission")
.WithOpenApi();

app.MapGet("/v1/submissions/{id:guid}", async (
    Guid id,
    SubmissionService service,
    CancellationToken cancellationToken) =>
{
    var submission = await service.GetSubmissionAsync(id, cancellationToken);
    return submission is null ? Results.NotFound() : Results.Ok(submission);
})
.WithName("GetSubmission")
.WithOpenApi();

app.MapGet("/v1/submissions/{id:guid}/matches", async (
    Guid id,
    SubmissionService service,
    CancellationToken cancellationToken) =>
{
    var matches = await service.GetMatchesAsync(id, cancellationToken);
    return matches is null ? Results.NotFound() : Results.Ok(matches);
})
.WithName("GetSubmissionMatches")
.WithOpenApi();

app.Run();

public partial class Program;

namespace ContentId.Api
{
    public sealed class ContentIdOptions
    {
        public const string SectionName = "ContentId";

        public string PostgresConnectionString { get; init; } =
            "Host=localhost;Port=5432;Username=contentid;Password=contentid;Database=contentid";

        public string MongoConnectionString { get; init; } = "mongodb://localhost:27017";

        public string MongoDatabase { get; init; } = "contentid";

        public string QueueName { get; init; } = "content-id-job-queue";

        public string AwsServiceUrl { get; init; } = "http://localhost:4566";

        public string AwsRegion { get; init; } = "us-east-1";

        public string ReferenceAssetsPath { get; init; } = "data/reference-assets/reference-assets.json";
    }
}

namespace ContentId.Api.Models
{
    public sealed record CreateSubmissionRequest(
        string Title,
        string SourcePlatform,
        string ContentType,
        int DurationSeconds,
        string FingerprintHash);

    public sealed record SubmissionResponse(
        Guid SubmissionId,
        string Title,
        string SourcePlatform,
        string ContentType,
        int DurationSeconds,
        string FingerprintHash,
        string Status,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    public sealed record MatchResponse(
        Guid SubmissionId,
        string Status,
        IReadOnlyCollection<MatchResult> Matches);

    public sealed record MatchResult(
        string ReferenceAssetId,
        string Title,
        string Owner,
        string RightsPolicy,
        double Confidence,
        string FingerprintHash);

    public sealed record ReferenceAsset(
        string ReferenceAssetId,
        string Title,
        string Owner,
        string FingerprintHash,
        string RightsPolicy);

    public sealed record SubmissionCreated(SubmissionResponse? Submission, Dictionary<string, string[]> Errors)
    {
        public bool IsValid => Errors.Count == 0;
    }

    public sealed record IdentificationJobMessage(Guid SubmissionId, string FingerprintHash, DateTimeOffset EnqueuedAt);
}

namespace ContentId.Api.Services
{
    public interface ISubmissionRepository
    {
        Task CreateAsync(SubmissionResponse submission, CancellationToken cancellationToken);
        Task<SubmissionResponse?> GetAsync(Guid submissionId, CancellationToken cancellationToken);
        Task<IReadOnlyCollection<MatchResult>?> GetMatchesAsync(Guid submissionId, CancellationToken cancellationToken);
    }

    public interface IFingerprintDocumentStore
    {
        Task StoreSubmissionDocumentAsync(SubmissionResponse submission, CancellationToken cancellationToken);
    }

    public interface IIdentificationQueue
    {
        Task EnqueueAsync(IdentificationJobMessage message, CancellationToken cancellationToken);
    }

    public interface IReferenceAssetCatalog
    {
        Task<IReadOnlyCollection<ReferenceAsset>> GetReferenceAssetsAsync(CancellationToken cancellationToken);
    }

    public sealed class SubmissionService(
        ISubmissionRepository submissions,
        IFingerprintDocumentStore documents,
        IIdentificationQueue queue,
        ActivitySource activitySource)
    {
        public async Task<SubmissionCreated> CreateSubmissionAsync(
            CreateSubmissionRequest request,
            CancellationToken cancellationToken)
        {
            var errors = Validate(request);
            if (errors.Count > 0)
            {
                return new SubmissionCreated(null, errors);
            }

            using var activity = activitySource.StartActivity("submission.create");
            var now = DateTimeOffset.UtcNow;
            var submission = new SubmissionResponse(
                Guid.NewGuid(),
                request.Title.Trim(),
                request.SourcePlatform.Trim(),
                request.ContentType.Trim().ToLowerInvariant(),
                request.DurationSeconds,
                request.FingerprintHash.Trim().ToLowerInvariant(),
                "queued",
                now,
                now);

            activity?.SetTag("submission.id", submission.SubmissionId.ToString());
            activity?.SetTag("content.type", submission.ContentType);
            activity?.SetTag("source.platform", submission.SourcePlatform);

            await submissions.CreateAsync(submission, cancellationToken);
            await documents.StoreSubmissionDocumentAsync(submission, cancellationToken);
            await queue.EnqueueAsync(
                new IdentificationJobMessage(submission.SubmissionId, submission.FingerprintHash, now),
                cancellationToken);

            return new SubmissionCreated(submission, []);
        }

        public Task<SubmissionResponse?> GetSubmissionAsync(Guid submissionId, CancellationToken cancellationToken) =>
            submissions.GetAsync(submissionId, cancellationToken);

        public async Task<MatchResponse?> GetMatchesAsync(Guid submissionId, CancellationToken cancellationToken)
        {
            var submission = await submissions.GetAsync(submissionId, cancellationToken);
            if (submission is null)
            {
                return null;
            }

            var matches = await submissions.GetMatchesAsync(submissionId, cancellationToken) ?? [];
            return new MatchResponse(submissionId, submission.Status, matches);
        }

        private static Dictionary<string, string[]> Validate(CreateSubmissionRequest request)
        {
            var errors = new Dictionary<string, string[]>();

            if (string.IsNullOrWhiteSpace(request.Title))
            {
                errors[nameof(request.Title)] = ["Title is required."];
            }

            if (string.IsNullOrWhiteSpace(request.SourcePlatform))
            {
                errors[nameof(request.SourcePlatform)] = ["SourcePlatform is required."];
            }

            var contentType = request.ContentType?.Trim().ToLowerInvariant();
            if (contentType is not ("audio" or "video"))
            {
                errors[nameof(request.ContentType)] = ["ContentType must be audio or video."];
            }

            if (request.DurationSeconds <= 0)
            {
                errors[nameof(request.DurationSeconds)] = ["DurationSeconds must be positive."];
            }

            if (string.IsNullOrWhiteSpace(request.FingerprintHash))
            {
                errors[nameof(request.FingerprintHash)] = ["FingerprintHash is required."];
            }

            return errors;
        }
    }

    public sealed class JsonReferenceAssetCatalog(IConfiguration configuration, IWebHostEnvironment environment)
        : IReferenceAssetCatalog
    {
        public async Task<IReadOnlyCollection<ReferenceAsset>> GetReferenceAssetsAsync(CancellationToken cancellationToken)
        {
            var options = configuration.GetSection(ContentIdOptions.SectionName).Get<ContentIdOptions>() ?? new();
            var path = Path.IsPathRooted(options.ReferenceAssetsPath)
                ? options.ReferenceAssetsPath
                : Path.Combine(environment.ContentRootPath, options.ReferenceAssetsPath);

            if (!File.Exists(path))
            {
                path = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", options.ReferenceAssetsPath));
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
}

namespace ContentId.Api.Infrastructure
{
    public interface IStorageInitializer
    {
        Task InitializeAsync();
    }

    public sealed class PostgresStorageInitializer(IConfiguration configuration) : IStorageInitializer
    {
        public async Task InitializeAsync()
        {
            var options = configuration.GetSection(ContentIdOptions.SectionName).Get<ContentIdOptions>() ?? new();
            await using var connection = new NpgsqlConnection(options.PostgresConnectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                create table if not exists submissions (
                    submission_id uuid primary key,
                    title text not null,
                    source_platform text not null,
                    content_type text not null,
                    duration_seconds integer not null,
                    fingerprint_hash text not null,
                    status text not null,
                    created_at timestamptz not null,
                    updated_at timestamptz not null
                );

                create table if not exists match_results (
                    submission_id uuid not null references submissions(submission_id),
                    reference_asset_id text not null,
                    title text not null,
                    owner text not null,
                    rights_policy text not null,
                    confidence double precision not null,
                    fingerprint_hash text not null,
                    primary key (submission_id, reference_asset_id)
                );
                """;
            await command.ExecuteNonQueryAsync();
        }
    }

    public sealed class NoopStorageInitializer : IStorageInitializer
    {
        public Task InitializeAsync() => Task.CompletedTask;
    }

    public sealed class PostgresSubmissionRepository(IConfiguration configuration) : ISubmissionRepository
    {
        private ContentIdOptions Options =>
            configuration.GetSection(ContentIdOptions.SectionName).Get<ContentIdOptions>() ?? new();

        public async Task CreateAsync(SubmissionResponse submission, CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(Options.PostgresConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                insert into submissions (
                    submission_id,
                    title,
                    source_platform,
                    content_type,
                    duration_seconds,
                    fingerprint_hash,
                    status,
                    created_at,
                    updated_at)
                values (
                    @submission_id,
                    @title,
                    @source_platform,
                    @content_type,
                    @duration_seconds,
                    @fingerprint_hash,
                    @status,
                    @created_at,
                    @updated_at);
                """;
            command.Parameters.AddWithValue("submission_id", submission.SubmissionId);
            command.Parameters.AddWithValue("title", submission.Title);
            command.Parameters.AddWithValue("source_platform", submission.SourcePlatform);
            command.Parameters.AddWithValue("content_type", submission.ContentType);
            command.Parameters.AddWithValue("duration_seconds", submission.DurationSeconds);
            command.Parameters.AddWithValue("fingerprint_hash", submission.FingerprintHash);
            command.Parameters.AddWithValue("status", submission.Status);
            command.Parameters.AddWithValue("created_at", submission.CreatedAt);
            command.Parameters.AddWithValue("updated_at", submission.UpdatedAt);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task<SubmissionResponse?> GetAsync(Guid submissionId, CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(Options.PostgresConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                select submission_id, title, source_platform, content_type, duration_seconds,
                       fingerprint_hash, status, created_at, updated_at
                from submissions
                where submission_id = @submission_id;
                """;
            command.Parameters.AddWithValue("submission_id", submissionId);

            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            return new SubmissionResponse(
                reader.GetGuid(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7),
                reader.GetFieldValue<DateTimeOffset>(8));
        }

        public async Task<IReadOnlyCollection<MatchResult>?> GetMatchesAsync(Guid submissionId, CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(Options.PostgresConnectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                select reference_asset_id, title, owner, rights_policy, confidence, fingerprint_hash
                from match_results
                where submission_id = @submission_id
                order by confidence desc;
                """;
            command.Parameters.AddWithValue("submission_id", submissionId);

            var matches = new List<MatchResult>();
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                matches.Add(new MatchResult(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetDouble(4),
                    reader.GetString(5)));
            }

            return matches;
        }
    }

    public sealed class MongoFingerprintDocumentStore(IConfiguration configuration) : IFingerprintDocumentStore
    {
        private ContentIdOptions Options =>
            configuration.GetSection(ContentIdOptions.SectionName).Get<ContentIdOptions>() ?? new();

        public async Task StoreSubmissionDocumentAsync(SubmissionResponse submission, CancellationToken cancellationToken)
        {
            var client = new MongoClient(Options.MongoConnectionString);
            var database = client.GetDatabase(Options.MongoDatabase);
            var collection = database.GetCollection<BsonDocument>("fingerprint_documents");
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

            await collection.InsertOneAsync(document, cancellationToken: cancellationToken);
        }
    }

    public sealed class SqsIdentificationQueue(IConfiguration configuration) : IIdentificationQueue
    {
        private ContentIdOptions Options =>
            configuration.GetSection(ContentIdOptions.SectionName).Get<ContentIdOptions>() ?? new();

        public async Task EnqueueAsync(IdentificationJobMessage message, CancellationToken cancellationToken)
        {
            var options = Options;
            using var client = new AmazonSQSClient(
                new BasicAWSCredentials("test", "test"),
                new AmazonSQSConfig
                {
                    ServiceURL = options.AwsServiceUrl,
                    AuthenticationRegion = options.AwsRegion,
                    UseHttp = options.AwsServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                });

            var queueUrl = await client.GetQueueUrlAsync(options.QueueName, cancellationToken);
            await client.SendMessageAsync(new SendMessageRequest
            {
                QueueUrl = queueUrl.QueueUrl,
                MessageBody = JsonSerializer.Serialize(message)
            }, cancellationToken);
        }
    }

    public sealed class InMemoryContentStore : ISubmissionRepository, IFingerprintDocumentStore
    {
        private readonly Dictionary<Guid, SubmissionResponse> _submissions = [];
        private readonly Dictionary<Guid, IReadOnlyCollection<MatchResult>> _matches = [];

        public Task CreateAsync(SubmissionResponse submission, CancellationToken cancellationToken)
        {
            _submissions[submission.SubmissionId] = submission;
            _matches[submission.SubmissionId] = [];
            return Task.CompletedTask;
        }

        public Task<SubmissionResponse?> GetAsync(Guid submissionId, CancellationToken cancellationToken)
        {
            _submissions.TryGetValue(submissionId, out var submission);
            return Task.FromResult(submission);
        }

        public Task<IReadOnlyCollection<MatchResult>?> GetMatchesAsync(Guid submissionId, CancellationToken cancellationToken)
        {
            _matches.TryGetValue(submissionId, out var matches);
            return Task.FromResult(matches);
        }

        public Task StoreSubmissionDocumentAsync(SubmissionResponse submission, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    public sealed class NoopIdentificationQueue : IIdentificationQueue
    {
        public Task EnqueueAsync(IdentificationJobMessage message, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    public sealed class DependencyHealthCheck(
        ISubmissionRepository submissions,
        IReferenceAssetCatalog referenceAssets)
        : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
    {
        public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
            Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await submissions.GetAsync(Guid.Empty, cancellationToken);
                await referenceAssets.GetReferenceAssetsAsync(cancellationToken);
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy();
            }
            catch (Exception ex)
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy(ex.Message, ex);
            }
        }
    }
}
