using System.Collections.Concurrent;

namespace Foundatio.Mediator.Distributed;

/// <summary>
/// Resolves message types from type names carried in message headers.
/// </summary>
/// <remarks>
/// Types registered during DI setup are always resolvable. Other names resolve only when the caller
/// supplies a type the result must be assignable to, which lets a handler declared on an interface
/// or base type receive concrete messages without opening arbitrary type loading.
/// </remarks>
public sealed class MessageTypeResolver
{
    private readonly ConcurrentDictionary<string, Type> _allowedTypes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Type> _resolvedTypes = new(StringComparer.Ordinal);

    /// <summary>
    /// Registers a type as allowed for deserialization.
    /// </summary>
    public void Register(Type type)
    {
        if (type.AssemblyQualifiedName is { } aqn)
            _allowedTypes.TryAdd(aqn, type);

        if (type.FullName is { } fullName)
            _allowedTypes.TryAdd(fullName, type);
    }

    /// <summary>
    /// Resolves a registered type by name, or <c>null</c> when it was not registered.
    /// </summary>
    public Type? TryResolve(string typeName)
        => _allowedTypes.TryGetValue(typeName, out var type) ? type : null;

    /// <summary>
    /// Resolves a type by name when it is registered or when it can be loaded and is assignable to
    /// <paramref name="assignableTo"/>. Returns <c>null</c> otherwise.
    /// </summary>
    public Type? TryResolve(string typeName, Type assignableTo)
    {
        if (string.IsNullOrEmpty(typeName) || typeName.Length > 2048)
            return null;
        var type = TryResolve(typeName) ?? (_resolvedTypes.TryGetValue(typeName, out var cached) ? cached : ResolveLoadedType(typeName));
        if (type is null || !assignableTo.IsAssignableFrom(type))
            return null;
        if (_resolvedTypes.Count < 1024)
            _resolvedTypes.TryAdd(typeName, type);
        return type;
    }

    private static Type? ResolveLoadedType(string typeName)
    {
        var type = Type.GetType(typeName, throwOnError: false);
        if (type is not null)
            return type;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic)
                continue;

            type = assembly.GetType(typeName, throwOnError: false);
            if (type is not null)
                return type;
        }

        return null;
    }
}
