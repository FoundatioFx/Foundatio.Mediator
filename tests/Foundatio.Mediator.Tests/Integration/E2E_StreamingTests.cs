using System.Runtime.CompilerServices;
using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests.Integration;

public class E2E_StreamingTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    public record CounterStreamQuery(int Count);

    public class CounterStreamHandler
    {
        public async IAsyncEnumerable<int> HandleAsync(
            CounterStreamQuery query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (int i = 0; i < query.Count; i++)
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                await Task.Yield();
                yield return i;
            }
        }
    }

    [Fact]
    public async Task StreamingHandler_ReturnsAllItems()
    {
        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<CounterStreamHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var items = new List<int>();
        await foreach (var item in mediator.Invoke<IAsyncEnumerable<int>>(new CounterStreamQuery(5), TestCancellationToken))
        {
            items.Add(item);
        }

        Assert.Equal([0, 1, 2, 3, 4], items);
    }

    [Fact]
    public async Task StreamingHandler_SupportsCancellation()
    {
        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<CounterStreamHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        using var cts = new CancellationTokenSource();
        var items = new List<int>();

        await foreach (var item in mediator.Invoke<IAsyncEnumerable<int>>(new CounterStreamQuery(100), cts.Token))
        {
            items.Add(item);
            if (items.Count == 3)
                cts.Cancel();
        }

        Assert.Equal(3, items.Count);
        Assert.Equal([0, 1, 2], items);
    }

    [Fact]
    public async Task StreamingHandler_EmptyStream_ReturnsNoItems()
    {
        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<CounterStreamHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var items = new List<int>();
        await foreach (var item in mediator.Invoke<IAsyncEnumerable<int>>(new CounterStreamQuery(0), TestCancellationToken))
        {
            items.Add(item);
        }

        Assert.Empty(items);
    }

    public record StringStreamQuery(string Prefix, int Count);

    public class StaticStringStreamHandler
    {
        public static async IAsyncEnumerable<string> Handle(
            StringStreamQuery query,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            for (int i = 0; i < query.Count; i++)
            {
                await Task.Yield();
                yield return $"{query.Prefix}-{i}";
            }
        }
    }

    [Fact]
    public async Task StaticStreamingHandler_ReturnsExpectedItems()
    {
        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<StaticStringStreamHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        var items = new List<string>();
        await foreach (var item in mediator.Invoke<IAsyncEnumerable<string>>(new StringStreamQuery("item", 3), TestCancellationToken))
        {
            items.Add(item);
        }

        Assert.Equal(["item-0", "item-1", "item-2"], items);
    }
}
