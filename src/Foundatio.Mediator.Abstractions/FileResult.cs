namespace Foundatio.Mediator;

/// <summary>
/// Represents a file to be returned from a handler. When used as the value in
/// <c>Result&lt;FileResult&gt;</c>, the generated endpoint streams the file to the client
/// with the appropriate content type and optional download disposition.
/// </summary>
/// <example>
/// <code>
/// public Result&lt;FileResult&gt; Handle(ExportReport query)
/// {
///     var stream = GenerateCsv(query.ReportId);
///     return Result.File(stream, "text/csv", "report.csv");
/// }
/// </code>
/// </example>
public sealed class FileResult
{
    /// <summary>
    /// The file content as a stream. The framework disposes the stream after the response is sent.
    /// </summary>
    public Stream Stream { get; init; } = Stream.Null;

    /// <summary>
    /// The MIME content type of the file (e.g. <c>"application/pdf"</c>, <c>"text/csv"</c>).
    /// </summary>
    public string ContentType { get; init; } = "application/octet-stream";

    /// <summary>
    /// Optional file name. When set, the response includes a <c>Content-Disposition: attachment</c>
    /// header that prompts a download. When <c>null</c>, the content is served inline.
    /// </summary>
    public string? FileName { get; init; }
}
