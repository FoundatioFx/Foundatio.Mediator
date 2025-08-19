using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator;

public sealed class HandlerScopeValue(IServiceScope scope, IDisposable disposable, CancellationToken token) : IDisposable
{
    public IServiceScope Scope { get; } = scope ?? throw new ArgumentNullException(nameof(scope));
    public IServiceProvider Services => Scope.ServiceProvider;
    public CancellationToken Token { get; } = token;

    public void Dispose()
    {
        disposable.Dispose();
    }
}

public static class HandlerScope
{
    private static readonly AsyncLocal<Stack<HandlerScopeValue>?> _stack = new();

    public static HandlerScopeValue? Current => _stack.Value is { Count: > 0 } ? _stack.Value.Peek() : null;

    public static HandlerScopeValue GetOrCreate(IMediator mediator, CancellationToken cancellationToken)
    {
        if (mediator is null) throw new ArgumentNullException(nameof(mediator));

        if (_stack.Value is null or { Count: 0 })
        {
            var serviceProvider = (IServiceProvider)mediator;
            _stack.Value ??= new Stack<HandlerScopeValue>(4);
            _stack.Value.Push(new HandlerScopeValue(serviceProvider.CreateScope(), new PopScope(), cancellationToken));
        }

        return _stack.Value!.Peek();
    }

    public static IDisposable Push(HandlerScopeValue value)
    {
        _stack.Value ??= new Stack<HandlerScopeValue>(4);
        _stack.Value.Push(value);
        return new PopScope();
    }

    private sealed class PopScope : IDisposable
    {
        public void Dispose()
        {
            var s = _stack.Value;
            if (s is { Count: > 0 })
                s.Pop();
        }
    }
}
