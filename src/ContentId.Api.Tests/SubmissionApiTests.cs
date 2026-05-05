using System.Net;
using System.Net.Http.Json;
using ContentId.Api.Models;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ContentId.Api.Tests;

public sealed class SubmissionApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public SubmissionApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithWebHostBuilder(builder => builder.UseSetting("environment", "Testing"));
    }

    [Fact]
    public async Task CreateSubmission_ReturnsCreatedSubmission()
    {
        using var client = _factory.CreateClient();
        var request = new CreateSubmissionRequest(
            "Launch Track",
            "PartnerUpload",
            "audio",
            187,
            "abc123");

        var response = await client.PostAsJsonAsync("/v1/submissions", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<SubmissionResponse>();
        Assert.NotNull(body);
        Assert.Equal("queued", body.Status);
        Assert.Equal("abc123", body.FingerprintHash);
    }

    [Fact]
    public async Task CreateSubmission_RejectsInvalidContentType()
    {
        using var client = _factory.CreateClient();
        var request = new CreateSubmissionRequest(
            "Unsupported Media",
            "PartnerUpload",
            "document",
            60,
            "abc123");

        var response = await client.PostAsJsonAsync("/v1/submissions", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetSubmission_ReturnsPreviouslyCreatedSubmission()
    {
        using var client = _factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync(
            "/v1/submissions",
            new CreateSubmissionRequest("Sample Clip", "UGCPlatform", "video", 42, "def456"));
        var created = await createResponse.Content.ReadFromJsonAsync<SubmissionResponse>();

        var getResponse = await client.GetAsync($"/v1/submissions/{created!.SubmissionId}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var fetched = await getResponse.Content.ReadFromJsonAsync<SubmissionResponse>();
        Assert.Equal(created.SubmissionId, fetched!.SubmissionId);
        Assert.Equal("video", fetched.ContentType);
    }
}
