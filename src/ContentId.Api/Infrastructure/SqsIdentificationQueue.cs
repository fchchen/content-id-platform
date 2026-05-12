using System.Text.Json;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using ContentId.Api.Models;
using ContentId.Api.Services;
using Microsoft.Extensions.Options;

namespace ContentId.Api.Infrastructure;

public sealed class SqsIdentificationQueue : IIdentificationQueue
{
    private readonly AmazonSQSClient _client;
    private readonly string _queueName;
    private string? _queueUrl;

    public SqsIdentificationQueue(IOptions<ContentIdOptions> options)
    {
        var settings = options.Value;
        _queueName = settings.QueueName;
        // LocalStack accepts static placeholder credentials. A production AWS endpoint should use
        // the default credential chain via an IAM role or environment-provided credentials.
        _client = new AmazonSQSClient(
            new BasicAWSCredentials("test", "test"),
            new AmazonSQSConfig
            {
                ServiceURL = settings.AwsServiceUrl,
                AuthenticationRegion = settings.AwsRegion,
                UseHttp = settings.AwsServiceUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            });
    }

    public async Task EnqueueAsync(IdentificationJobMessage message, CancellationToken cancellationToken)
    {
        _queueUrl ??= (await _client.GetQueueUrlAsync(_queueName, cancellationToken)).QueueUrl;
        await _client.SendMessageAsync(new SendMessageRequest
        {
            QueueUrl = _queueUrl,
            MessageBody = JsonSerializer.Serialize(message)
        }, cancellationToken);
    }
}
