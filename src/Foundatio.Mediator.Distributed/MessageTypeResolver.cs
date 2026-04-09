using System.Collections.Concurrent;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Resolves message types from assembly-qualified type names using a pre-registered allowlist.
/// Prevents arbitrary type loading from untrusted message headers.
/// </summary>
/// <remarks>
/// The resolver is populated during DI registration from handler registrations and known
/// notification types. Only types that have been explicitly registered can be deserialized.
/// </remarks>
public sealed class MessageTypeResolver
{
    private readonly ConcurrentDictionary<string, Type> _allowedTypes = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a type as allowed for deserialization.
    /// </summary>
    public void Register(Type type)
    {
        var key = type.AssemblyQualifiedName;
        if (key is not null)
            _allowedTypes.TryAdd(key, type);

        // Also register by full name for resilience against assembly version changes
        var fullName = type.FullName;
        if (fullName is not null)
            _allowedTypes.TryAdd(fullName, type);
    }

    /// <summary>
    /// Attempts to resolve a type from a type name. Returns <c>null</c> if the type
    /// is not in the allowlist.
    /// </summary>
    public Type? TryResolve(string typeName)
    {
        if (_allowedTypes.TryGetValue(typeName, out var type))
            return type;

        return null;
    }
}
