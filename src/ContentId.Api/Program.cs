using System.Diagnostics;
using System.Diagnostics.Metrics;
using ContentId.Api;
using ContentId.Api.Infrastructure;
using ContentId.Api.Models;
using ContentId.Api.Services;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<ContentIdOptions>(builder.Configuration.GetSection(ContentIdOptions.SectionName));
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
var apiMeter = new Meter("ContentId.Api");
var submissionsCreated = apiMeter.CreateCounter<long>("contentid.submissions.created", "count", "Total submissions received");

builder.Services.AddSingleton(new ActivitySource("ContentId.Api"));
builder.Services.AddSingleton(apiMeter);

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
    builder.Services.AddSingleton<ISubmissionRepository, SqlServerSubmissionRepository>();
    builder.Services.AddSingleton<IFingerprintDocumentStore, MongoFingerprintDocumentStore>();
    builder.Services.AddSingleton<IIdentificationQueue, SqsIdentificationQueue>();
    builder.Services.AddSingleton<IStorageInitializer, SqlServerStorageInitializer>();
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
    })
    .WithMetrics(metrics =>
    {
        metrics.AddMeter("Microsoft.AspNetCore.Hosting");
        metrics.AddMeter("ContentId.Api");
        metrics.AddOtlpExporter();
    });

builder.Logging.AddOpenTelemetry(options =>
{
    options.SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("content-id-api"));
    options.IncludeFormattedMessage = true;
    options.AddOtlpExporter();
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
    if (result.IsValid)
    {
        submissionsCreated.Add(1);
        return Results.Created($"/v1/submissions/{result.Submission!.SubmissionId}", result.Submission);
    }
    return Results.ValidationProblem(result.Errors);
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
