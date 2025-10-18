using ConsoleSample.Messages;
using Microsoft.Extensions.Logging;

namespace ConsoleSample.Middleware;

public class TransactionMiddleware
{
    public ITransaction Before(CreateOrder cmd, ILogger<TransactionMiddleware> logger)
    {
        var transaction = new FakeTransaction();
        logger.LogInformation("Transaction started: {TransactionId}", transaction.Id);
        return transaction;
    }

    public void Finally(CreateOrder cmd, ITransaction transaction, ILogger<TransactionMiddleware> logger)
    {
        logger.LogInformation("Transaction committed: {TransactionId}", transaction.Id);
        transaction.Dispose();
    }
}

public interface ITransaction : IDisposable
{
    string Id { get; }
}

public class FakeTransaction : ITransaction
{
    public string Id { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);
    public void Dispose() { }
}
