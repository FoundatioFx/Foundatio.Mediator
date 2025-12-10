namespace Foundatio.Mediator;

/// <summary>
/// Contains information about the handler that is currently executing.
/// Can be added as a parameter to middleware methods to access handler metadata.
/// </summary>
public class HandlerExecutionInfo
{
    /// <summary>
    /// Creates a new HandlerExecutionInfo instance
    /// </summary>
    /// <param name="handlerType">The type of the handler</param>
    /// <param name="handlerMethod">The method being invoked on the handler</param>
    public HandlerExecutionInfo(Type handlerType, MethodInfo handlerMethod)
    {
        HandlerType = handlerType ?? throw new ArgumentNullException(nameof(handlerType));
        HandlerMethod = handlerMethod ?? throw new ArgumentNullException(nameof(handlerMethod));
    }

    /// <summary>
    /// The type of the handler class
    /// </summary>
    public Type HandlerType { get; }

    /// <summary>
    /// The method being invoked on the handler
    /// </summary>
    public MethodInfo HandlerMethod { get; }
}
