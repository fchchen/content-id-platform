using ContentId.Api.Models;
using ContentId.Api.Services;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ContentId.Api.Infrastructure;

public sealed class SqlServerSubmissionRepository(IOptions<ContentIdOptions> options) : ISubmissionRepository
{
    public async Task CreateAsync(SubmissionResponse submission, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.Value.SqlServerConnectionString);
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
        command.Parameters.AddWithValue("@submission_id", submission.SubmissionId);
        command.Parameters.AddWithValue("@title", submission.Title);
        command.Parameters.AddWithValue("@source_platform", submission.SourcePlatform);
        command.Parameters.AddWithValue("@content_type", submission.ContentType);
        command.Parameters.AddWithValue("@duration_seconds", submission.DurationSeconds);
        command.Parameters.AddWithValue("@fingerprint_hash", submission.FingerprintHash);
        command.Parameters.AddWithValue("@status", submission.Status);
        command.Parameters.AddWithValue("@created_at", submission.CreatedAt);
        command.Parameters.AddWithValue("@updated_at", submission.UpdatedAt);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SubmissionResponse?> GetAsync(Guid submissionId, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(options.Value.SqlServerConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select submission_id, title, source_platform, content_type, duration_seconds,
                   fingerprint_hash, status, created_at, updated_at
            from submissions
            where submission_id = @submission_id;
            """;
        command.Parameters.AddWithValue("@submission_id", submissionId);

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
        await using var connection = new SqlConnection(options.Value.SqlServerConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select reference_asset_id, title, owner, rights_policy, confidence, fingerprint_hash
            from match_results
            where submission_id = @submission_id
            order by confidence desc;
            """;
        command.Parameters.AddWithValue("@submission_id", submissionId);

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
