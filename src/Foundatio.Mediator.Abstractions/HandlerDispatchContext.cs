namespace Foundatio.Mediator;

/// <summary>Marks a delivery that a dispatcher has handed to its processing host.</summary>
public sealed class HandlerDispatchContext
{
    private HandlerDispatchContext() { }

    /// <summary>
    /// Set this in CallContext when invoking the processing pipeline in an already-created scope.
    /// The host owns that scope and disposes it after processing and cascading messages finish.
    /// </summary>
    public static HandlerDispatchContext Processing { get; } = new();

    /// <summary>Whether the invocation is executing the processing stage.</summary>
    public static bool IsProcessing(CallContext? context) => context?.Get(typeof(HandlerDispatchContext)) is HandlerDispatchContext;
}
