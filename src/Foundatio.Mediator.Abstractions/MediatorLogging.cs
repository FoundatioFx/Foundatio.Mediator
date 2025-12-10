using Microsoft.Extensions.Logging;

namespace Foundatio.Mediator;

/// <summary>
/// High-performance logging for the mediator using cached LoggerMessage delegates.
/// This avoids allocations and string parsing on every log call.
/// </summary>
public static class MediatorLogging
{
    private static readonly Action<ILogger, string, Exception?> s_processingMessage =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(1, "ProcessingMessage"), "Processing message {MessageType}");

    private static readonly Action<ILogger, string, Exception?> s_completedMessage =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(2, "CompletedMessage"), "Completed processing message {MessageType}");

    private static readonly Action<ILogger, string, Exception?> s_shortCircuitedMessage =
        LoggerMessage.Define<string>(LogLevel.Debug, new EventId(3, "ShortCircuitedMessage"), "Short-circuited processing message {MessageType}");

    /// <summary>
    /// Logs that a message is being processed.
    /// </summary>
    public static void LogProcessingMessage(this ILogger logger, string messageType)
        => s_processingMessage(logger, messageType, null);

    /// <summary>
    /// Logs that a message has completed processing.
    /// </summary>
    public static void LogCompletedMessage(this ILogger logger, string messageType)
        => s_completedMessage(logger, messageType, null);

    /// <summary>
    /// Logs that message processing was short-circuited by middleware.
    /// </summary>
    public static void LogShortCircuitedMessage(this ILogger logger, string messageType)
        => s_shortCircuitedMessage(logger, messageType, null);
}
