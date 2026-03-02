namespace Foundatio.Mediator;

/// <summary>
/// Contains information about the handler that is currently executing.
/// Can be added as a parameter to middleware methods to access handler metadata.
/// </summary>
public sealed class HandlerExecutionInfo
{
    /// <summary>
    /// Creates a new HandlerExecutionInfo instance.
    /// </summary>
    /// <param name="handlerType">The type of the handler.</param>
    /// <param name="handlerMethod">The method being invoked on the handler.</param>
    public HandlerExecutionInfo(Type handlerType, MethodInfo handlerMethod)
        : this(handlerType, handlerMethod, AuthorizationRequirements.Default)
    {
    }

    /// <summary>
    /// Creates a new HandlerExecutionInfo instance with authorization requirements.
    /// </summary>
    /// <param name="handlerType">The type of the handler.</param>
    /// <param name="handlerMethod">The method being invoked on the handler.</param>
    /// <param name="authorization">The authorization requirements for this handler.</param>
    public HandlerExecutionInfo(Type handlerType, MethodInfo handlerMethod, AuthorizationRequirements authorization)
    {
        HandlerType = handlerType ?? throw new ArgumentNullException(nameof(handlerType));
        HandlerMethod = handlerMethod ?? throw new ArgumentNullException(nameof(handlerMethod));
        Authorization = authorization ?? AuthorizationRequirements.Default;
    }

    /// <summary>
    /// The type of the handler class.
    /// </summary>
    public Type HandlerType { get; }

    /// <summary>
    /// The method being invoked on the handler.
    /// </summary>
    public MethodInfo HandlerMethod { get; }

    /// <summary>
    /// The authorization requirements for this handler.
    /// Always non-null; uses <see cref="AuthorizationRequirements.Default"/> when no authorization is configured.
    /// </summary>
    public AuthorizationRequirements Authorization { get; }
}
