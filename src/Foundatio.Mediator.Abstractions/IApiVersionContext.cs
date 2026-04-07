namespace Foundatio.Mediator;

/// <summary>
/// Provides access to the current API version for the request. Injectable into handlers
/// and middleware to enable version-dependent behavior without requiring separate handler classes.
/// </summary>
/// <example>
/// <code>
/// public class ProductHandler
/// {
///     public Task&lt;Result&lt;object&gt;&gt; HandleAsync(GetProduct query, IApiVersionContext version, ...)
///     {
///         var product = await repo.GetById(query.Id);
///         return version.IsBefore("2") ? new ProductV1Dto(product) : new ProductV2Dto(product);
///     }
/// }
/// </code>
/// </example>
public interface IApiVersionContext
{
    /// <summary>
    /// The API version for the current request, resolved from the version header.
    /// When no header is sent, this is the latest (default) version.
    /// </summary>
    string Current { get; }

    /// <summary>
    /// Returns <c>true</c> if the current version is before the specified version.
    /// Supports both integer ("1", "2") and date ("2024-01-15") version formats.
    /// </summary>
    bool IsBefore(string version);

    /// <summary>
    /// Returns <c>true</c> if the current version is the specified version or later.
    /// Supports both integer ("1", "2") and date ("2024-01-15") version formats.
    /// </summary>
    bool IsAtLeast(string version);

    /// <summary>
    /// Returns <c>true</c> if the current version exactly matches the specified version.
    /// </summary>
    bool Is(string version);
}
