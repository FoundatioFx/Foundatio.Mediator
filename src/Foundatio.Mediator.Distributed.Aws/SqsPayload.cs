using System.Text;
using System.Text.Json;

namespace Foundatio.Mediator.Distributed.Aws;

/// <summary>
/// Body encoding and size rules shared by the SQS queue and SNS pub/sub clients.
/// Bodies travel as UTF-8 JSON text; headers travel as message attributes, packed into one JSON attribute
/// when there are more than SQS allows (a traced, tracked, dead-lettered message already exceeds ten).
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
    /// The attribute values to send for <paramref name="headers"/>: one per header while they fit, otherwise a
    /// single <see cref="MessageHeaders.PackedHeaders"/> attribute holding all of them.
    /// </summary>
    public static Dictionary<string, string> ToAttributeValues(IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null || headers.Count == 0)
            return [];

        if (headers.Count <= MaxMessageAttributes)
            return new Dictionary<string, string>(headers);

        return new Dictionary<string, string> { [MessageHeaders.PackedHeaders] = JsonSerializer.Serialize(headers) };
    }

    /// <summary>
    /// Expands a <see cref="MessageHeaders.PackedHeaders"/> attribute back into individual headers.
    /// </summary>
    public static void UnpackHeaders(Dictionary<string, string> headers)
    {
        if (!headers.Remove(MessageHeaders.PackedHeaders, out var packed) || string.IsNullOrEmpty(packed))
            return;

        var unpacked = JsonSerializer.Deserialize<Dictionary<string, string>>(packed);
        if (unpacked is null)
            return;

        foreach (var (key, value) in unpacked)
            headers[key] = value;
    }

    /// <summary>
    /// Returns the approximate wire size of the message and throws when it cannot be sent.
    /// </summary>
    public static int Validate(string destinationKind, string destination, string body, IReadOnlyDictionary<string, string>? headers)
    {
        int size = Encoding.UTF8.GetByteCount(body);
        foreach (var (key, value) in ToAttributeValues(headers))
            size += Encoding.UTF8.GetByteCount(key) + Encoding.UTF8.GetByteCount(value ?? string.Empty);

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
