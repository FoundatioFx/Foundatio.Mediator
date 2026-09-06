using Amazon;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;

namespace DistributedBenchmarks;

internal sealed class BrokerResources(Settings settings) : IDisposable
{
    private readonly IAmazonSQS _sqs = CreateSqs(settings);
    private readonly IAmazonSimpleNotificationService _sns = CreateSns(settings);

    public static IAmazonSQS CreateSqs(Settings settings) => settings.ServiceUrl is null
        ? new AmazonSQSClient(RegionEndpoint.GetBySystemName(settings.Region))
        : new AmazonSQSClient(new BasicAWSCredentials("test", "test"), new AmazonSQSConfig { ServiceURL = settings.ServiceUrl, AuthenticationRegion = settings.Region });
    public static IAmazonSimpleNotificationService CreateSns(Settings settings) => settings.ServiceUrl is null
        ? new AmazonSimpleNotificationServiceClient(RegionEndpoint.GetBySystemName(settings.Region))
        : new AmazonSimpleNotificationServiceClient(new BasicAWSCredentials("test", "test"), new AmazonSimpleNotificationServiceConfig { ServiceURL = settings.ServiceUrl, AuthenticationRegion = settings.Region });

    private bool Owned(string name) => name.StartsWith(settings.RunId + "-", StringComparison.Ordinal) || name.StartsWith(settings.RunId + "_", StringComparison.Ordinal);

    private async Task<List<string>> QueuesAsync(CancellationToken ct)
    {
        var urls = new List<string>();
        string? token = null;
        do
        {
            var response = await _sqs.ListQueuesAsync(new ListQueuesRequest { QueueNamePrefix = settings.RunId, NextToken = token, MaxResults = 1000 }, ct);
            urls.AddRange((response.QueueUrls ?? []).Where(url => Owned(new Uri(url).Segments[^1])));
            token = response.NextToken;
        } while (!string.IsNullOrEmpty(token));
        return urls;
    }

    public async Task DrainAsync(CancellationToken ct)
    {
        var urls = await QueuesAsync(ct);
        if (urls.Count == 0) throw new InvalidOperationException("The broker has no queues for this run; check transport addressing.");
        int empty = 0;
        while (empty < 2)
        {
            long pending = 0;
            foreach (string url in urls)
            {
                var response = await _sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
                {
                    QueueUrl = url,
                    AttributeNames = ["ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible", "ApproximateNumberOfMessagesDelayed"]
                }, ct);
                long depth = response.Attributes.Values.Sum(v => long.Parse(v, System.Globalization.CultureInfo.InvariantCulture));
                string name = new Uri(url).Segments[^1];
                if (depth > 0 && (name.EndsWith("-dead-letter", StringComparison.Ordinal) || name.EndsWith("_error", StringComparison.Ordinal) || name.EndsWith("_skipped", StringComparison.Ordinal)))
                    throw new InvalidOperationException($"Broker failure queue {name} contains {depth} messages.");
                pending += depth;
            }
            empty = pending == 0 ? empty + 1 : 0;
            if (empty < 2) await Task.Delay(250, ct);
        }
    }

    public async Task CleanupAsync(CancellationToken ct)
    {
        // Scope every deletion to this generated run id; never purge shared or unrelated resources.
        foreach (string url in await QueuesAsync(ct)) await _sqs.DeleteQueueAsync(url, ct);
        string? token = null;
        do
        {
            var response = await _sns.ListTopicsAsync(new ListTopicsRequest { NextToken = token }, ct);
            foreach (var topic in response.Topics ?? [])
                if (Owned(topic.TopicArn.Split(':')[^1])) await _sns.DeleteTopicAsync(topic.TopicArn, ct);
            token = response.NextToken;
        } while (!string.IsNullOrEmpty(token));
    }

    public void Dispose() { _sqs.Dispose(); _sns.Dispose(); }
}
