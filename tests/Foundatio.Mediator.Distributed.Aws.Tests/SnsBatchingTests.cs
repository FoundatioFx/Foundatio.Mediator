using System.Threading.Channels;
using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Microsoft.Extensions.Logging.Abstractions;

namespace Foundatio.Mediator.Distributed.Aws.Tests;

public class SnsBatchingTests
{
    [Fact]
    public async Task ConcurrentPublications_PreserveIndividualBrokerResults()
    {
        var ct = TestContext.Current.CancellationToken;
        using var sns = new ControlledSns();
        using var sqs = new AmazonSQSClient(new BasicAWSCredentials("test", "test"), new AmazonSQSConfig { ServiceURL = "http://localhost:1" });
        await using var client = new SqsPubSubClient(sns, sqs, new SqsPubSubClientOptions
        {
            TopicArn = "arn:aws:sns:us-east-1:000000000000:test",
            Batching = new AwsBatchOptions { MaxDelay = TimeSpan.FromSeconds(10) }
        }, new DistributedNotificationOptions(), NullLogger<SqsPubSubClient>.Instance);
        var publications = Enumerable.Range(0, 10).Select(i => client.PublishAsync("topic", [new PubSubEntry { Body = new byte[] { (byte)('a' + i) } }], ct)).ToArray();
        var request = await sns.Requests.Reader.ReadAsync(ct);
        Assert.Equal(10, request.PublishBatchRequestEntries.Count);
        Assert.All(publications, task => Assert.False(task.IsCompleted));
        sns.Response.SetResult(new PublishBatchResponse
        {
            Successful = request.PublishBatchRequestEntries.Where(e => e.Message != "d").Select(e => new PublishBatchResultEntry { Id = e.Id }).ToList(),
            Failed = [new BatchResultErrorEntry { Id = request.PublishBatchRequestEntries.Single(e => e.Message == "d").Id, Code = "Rejected", Message = "test failure" }]
        });
        for (int i = 0; i < publications.Length; i++)
        {
            if (i == 3) Assert.Contains("Rejected", (await Assert.ThrowsAsync<InvalidOperationException>(() => publications[i])).Message);
            else await publications[i].WaitAsync(ct);
        }
    }

    private sealed class ControlledSns() : AmazonSimpleNotificationServiceClient(new BasicAWSCredentials("test", "test"), new AmazonSimpleNotificationServiceConfig { ServiceURL = "http://localhost:1" })
    {
        public Channel<PublishBatchRequest> Requests { get; } = Channel.CreateUnbounded<PublishBatchRequest>();
        public TaskCompletionSource<PublishBatchResponse> Response { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override Task<PublishBatchResponse> PublishBatchAsync(PublishBatchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Writer.TryWrite(request);
            return Response.Task;
        }
    }
}
