using Foundatio.Mediator.Distributed;
using Foundatio.Mediator.Distributed.Redis;
using Foundatio.Mediator.Distributed.Tests;

namespace Foundatio.Mediator.Distributed.Redis.Tests;

public class RedisJobStateContractTests(RedisFixture fixture) : QueueJobStateStoreContractTests, IClassFixture<RedisFixture>
{
    protected override IQueueJobStateStore CreateStore() => new RedisQueueJobStateStore(fixture.Connection,
        new RedisJobStateStoreOptions { KeyPrefix = $"test:{Guid.NewGuid():N}" });
}
