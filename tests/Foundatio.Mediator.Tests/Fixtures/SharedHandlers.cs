namespace Foundatio.Mediator.Tests.Fixtures;

public class PingHandler
{
    public Task<string> HandleAsync(Ping message, CancellationToken ct) => Task.FromResult(message.Message + " Pong");
}

public class EchoHandler
{
    public Task HandleAsync(Echo message, CancellationToken ct) => Task.CompletedTask;
}
