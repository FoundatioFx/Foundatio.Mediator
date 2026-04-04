using System.Diagnostics;

namespace Foundatio.Mediator;

/// <summary>
/// Wraps a subscription message with metadata from the publisher, such as trace context.
/// </summary>
/// <remarks>
/// Use <c>mediator.SubscribeAsync&lt;MessageContext&lt;T&gt;&gt;()</c> instead of
/// <c>mediator.SubscribeAsync&lt;T&gt;()</c> to receive publisher metadata alongside messages.
/// </remarks>
public readonly struct MessageContext<T>
{
    public MessageContext(T message, ActivityContext activityContext)
    {
        Message = message;
        ActivityContext = activityContext;
    }

    /// <summary>The notification message.</summary>B
    public T Message { get; }

    /// <summary>
    /// The <see cref="System.Diagnostics.ActivityContext"/> that was active when the message
    /// was published. <c>default</c> when no trace was active.
    /// </summary>
    public ActivityContext ActivityContext { get; }
}
