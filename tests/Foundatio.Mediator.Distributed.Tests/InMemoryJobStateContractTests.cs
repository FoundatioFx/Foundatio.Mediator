namespace Foundatio.Mediator.Distributed.Tests;

public class InMemoryJobStateContractTests : QueueJobStateStoreContractTests
{
    protected override IQueueJobStateStore CreateStore() => new InMemoryQueueJobStateStore();
}
