using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;

namespace ContentId.Api.Infrastructure;

public sealed class SqlServerStorageInitializer(IOptions<ContentIdOptions> options) : IStorageInitializer
{
    public async Task InitializeAsync()
    {
        await using var connection = new SqlConnection(options.Value.SqlServerConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            if object_id(N'dbo.submissions', N'U') is null
            begin
                create table dbo.submissions (
                    submission_id uniqueidentifier not null primary key,
                    title nvarchar(256) not null,
                    source_platform nvarchar(128) not null,
                    content_type nvarchar(32) not null,
                    duration_seconds int not null,
                    fingerprint_hash nvarchar(256) not null,
                    status nvarchar(32) not null,
                    created_at datetimeoffset not null,
                    updated_at datetimeoffset not null
                );
            end;

            if object_id(N'dbo.match_results', N'U') is null
            begin
                create table dbo.match_results (
                    submission_id uniqueidentifier not null references dbo.submissions(submission_id),
                    reference_asset_id nvarchar(128) not null,
                    title nvarchar(256) not null,
                    owner nvarchar(256) not null,
                    rights_policy nvarchar(128) not null,
                    confidence float not null,
                    fingerprint_hash nvarchar(256) not null,
                    primary key (submission_id, reference_asset_id)
                );
            end;
            """;
        await command.ExecuteNonQueryAsync();
    }
}
