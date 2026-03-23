namespace Foundatio.Mediator;

/// <summary>
/// Endpoint metadata used by the generated API version matcher policy to select
/// the correct endpoint based on the request's version header.
/// Attached to endpoints via <c>.WithMetadata(new ApiVersionMetadata(...))</c>.
/// </summary>
public sealed class ApiVersionMetadata
{
    /// <summary>
    /// The API versions this endpoint serves. Empty means the endpoint is an
    /// unversioned fallback (matches when no versioned endpoint matches).
    /// </summary>
    public string[] Versions { get; }

    /// <summary>
    /// The HTTP header name used to read the requested API version.
    /// </summary>
    public string VersionHeader { get; }

    /// <summary>
    /// The default version to assume when no version header is present.
    /// Typically the latest declared version.
    /// </summary>
    public string DefaultVersion { get; }

    public ApiVersionMetadata(string[] versions, string versionHeader, string defaultVersion)
    {
        Versions = versions;
        VersionHeader = versionHeader;
        DefaultVersion = defaultVersion;
    }

    /// <summary>
    /// Resolves which candidate endpoint should handle the request based on version matching.
    /// </summary>
    /// <param name="candidateVersions">
    /// Per-candidate version arrays. <c>null</c> means skip (not a versioned endpoint or invalid candidate),
    /// empty array means unversioned fallback, non-empty means versioned endpoint.
    /// </param>
    /// <param name="requestedVersion">The version from the request header, or the default version.</param>
    /// <param name="allDeclaredVersions">
    /// The full ordered list of declared API versions (from MediatorConfiguration).
    /// Used for fallback when the requested version doesn't match any candidate.
    /// </param>
    /// <returns>
    /// A tuple of (Winner, HasVersioned) where Winner is the index of the winning candidate
    /// (-1 if no winner found) and HasVersioned indicates whether any versioned candidates existed.
    /// </returns>
    public static (int Winner, bool HasVersioned) ResolveWinner(
        string[]?[] candidateVersions,
        string requestedVersion,
        string[] allDeclaredVersions)
    {
        var hasVersioned = false;
        var matchingVersioned = -1;
        var fallback = -1;

        for (var i = 0; i < candidateVersions.Length; i++)
        {
            var versions = candidateVersions[i];
            if (versions == null)
                continue;

            if (versions.Length > 0)
            {
                hasVersioned = true;
                if (matchingVersioned < 0)
                {
                    for (var v = 0; v < versions.Length; v++)
                    {
                        if (string.Equals(versions[v], requestedVersion, StringComparison.OrdinalIgnoreCase))
                        {
                            matchingVersioned = i;
                            break;
                        }
                    }
                }
            }
            else
            {
                fallback = i;
            }
        }

        if (!hasVersioned)
            return (-1, false);

        if (matchingVersioned >= 0)
            return (matchingVersioned, true);

        if (fallback >= 0)
            return (fallback, true);

        // No exact match and no unversioned fallback — walk declared versions backwards
        // to find the latest version that any candidate serves.
        for (var d = allDeclaredVersions.Length - 1; d >= 0; d--)
        {
            var candidate = allDeclaredVersions[d];
            if (string.Equals(candidate, requestedVersion, StringComparison.OrdinalIgnoreCase))
                continue; // Already tried this one

            for (var i = 0; i < candidateVersions.Length; i++)
            {
                var versions = candidateVersions[i];
                if (versions == null || versions.Length == 0)
                    continue;

                for (var v = 0; v < versions.Length; v++)
                {
                    if (string.Equals(versions[v], candidate, StringComparison.OrdinalIgnoreCase))
                        return (i, true);
                }
            }
        }

        return (-1, true);
    }
}
