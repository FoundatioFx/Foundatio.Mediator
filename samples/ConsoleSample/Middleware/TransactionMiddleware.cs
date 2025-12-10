using System.Data;
using ConsoleSample.Messages;
using Foundatio.Mediator;
using Microsoft.Extensions.Logging;

namespace ConsoleSample.Middleware;

public class TransactionMiddleware
{
    public IDbTransaction Before(CreateOrder cmd, ILogger<TransactionMiddleware> logger)
    {
        var transaction = new FakeTransaction();
        logger.LogInformation("Transaction started: {TransactionId}", transaction.Id);
        return transaction;
    }

    public void After(CreateOrder cmd, IDbTransaction transaction, ILogger<TransactionMiddleware> logger)
    {
        var tx = (FakeTransaction)transaction;
        transaction.Commit();
        logger.LogInformation("Transaction committed: {TransactionId}", tx.Id);
    }

    public void Finally(CreateOrder cmd, Result? result, IDbTransaction? transaction, ILogger<TransactionMiddleware> logger)
    {
        if (transaction == null)
            return;

        var tx = (FakeTransaction)transaction;
        if (result?.IsSuccess == true)
            return;

        logger.LogInformation("Transaction rolled back: {TransactionId}", tx.Id);
        transaction.Rollback();
    }
}

public class FakeTransaction : IDbTransaction
{
    public string Id { get; } = Guid.NewGuid().ToString("N").Substring(0, 8);

    public IDbConnection? Connection => throw new NotImplementedException();

    public IsolationLevel IsolationLevel => throw new NotImplementedException();

    public void Commit() { }

    public void Dispose() { }

    public void Rollback() { }
}
