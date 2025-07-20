namespace ConsoleSample.Messages;

// Event for multiple handlers (Publish pattern)
public record OrderCreatedEvent(
    string OrderId,
    string CustomerId,
    decimal Amount,
    string ProductName
);

// Command for single handler (Invoke pattern)
public record ProcessOrderCommand(string OrderId, string ProcessingType);
