using System.Text;

namespace Foundatio.Mediator.Distributed.Aws;

/// <summary>
/// Body encoding and size rules shared by the SQS queue and SNS pub/sub clients.
/// Bodies travel as UTF-8 JSON text; headers travel as message attributes.
/// </summary>
internal static class SqsPayload
{
    /// <summary>
    /// SQS and SNS reject messages over 256 KiB (body plus attributes).
    /// </summary>
    public const int MaxMessageBytes = 262_144;

    /// <summary>
    /// SQS and SNS allow at most 10 message attributes per message.
    /// </summary>
    public const int MaxMessageAttributes = 10;

    public static string EncodeBody(ReadOnlyMemory<byte> body) => Encoding.UTF8.GetString(body.Span);

    public static byte[] DecodeBody(string? body) => string.IsNullOrEmpty(body) ? [] : Encoding.UTF8.GetBytes(body);

    /// <summary>
    /// Returns the approximate wire size of the message and throws when it cannot be sent.
    /// </summary>
    public static int Validate(string destinationKind, string destination, string body, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is { Count: > MaxMessageAttributes })
            throw new InvalidOperationException(
                $"Message for {destinationKind} '{destination}' has {headers.Count} headers, but SQS allows a maximum of {MaxMessageAttributes} message attributes.");

        int size = Encoding.UTF8.GetByteCount(body);
        if (headers is not null)
        {
            foreach (var (key, value) in headers)
                size += Encoding.UTF8.GetByteCount(key) + Encoding.UTF8.GetByteCount(value ?? string.Empty);
        }

        if (size > MaxMessageBytes)
        {
            string type = headers is not null && headers.TryGetValue(MessageHeaders.MessageType, out var messageType)
                ? $" ({MessageHeaders.MessageType}={messageType})"
                : string.Empty;
            throw new InvalidOperationException(
                $"Message for {destinationKind} '{destination}'{type} is {size:N0} bytes, over the SQS limit of {MaxMessageBytes:N0} bytes.");
        }

        return size;
    }
}
