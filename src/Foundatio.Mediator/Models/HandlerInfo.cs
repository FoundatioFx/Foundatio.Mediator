using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator.Models;

internal readonly record struct HandlerInfo
{
    public string Identifier { get; init; }
    public string FullName { get; init; }
    public string MethodName { get; init; }
    public TypeSymbolInfo MessageType { get; init; }
    public EquatableArray<string> MessageInterfaces { get; init; }
    public EquatableArray<string> MessageBaseClasses { get; init; }
    public bool HasReturnValue => !ReturnType.IsVoid;
    public TypeSymbolInfo ReturnType { get; init; }
    public bool IsAsync => ReturnType.IsTask || ReturnType.IsTuple || Middleware.Any(m => m.IsAsync);
    public bool IsStatic { get; init; }
    public EquatableArray<ParameterInfo> Parameters { get; init; }
    public EquatableArray<CallSiteInfo> CallSites { get; init; }
    public EquatableArray<MiddlewareInfo> Middleware { get; init; }
    public bool IsGenericHandlerClass { get; init; }
    public int GenericArity { get; init; }
    public EquatableArray<string> GenericTypeParameters { get; init; }
    public string? MessageGenericTypeDefinitionFullName { get; init; }
    public int MessageGenericArity { get; init; }
    public EquatableArray<string> GenericConstraints { get; init; }
    /// <summary>
    /// Whether this handler was discovered via explicit means (IHandler interface or [Handler] attribute)
    /// rather than conventional discovery (class name ending with Handler/Consumer).
    /// </summary>
    public bool IsExplicitlyDeclared { get; init; }

    /// <summary>
    /// Whether this handler class has constructor parameters (indicating DI dependencies).
    /// </summary>
    public bool HasConstructorParameters { get; init; }

    /// <summary>
    /// Whether this handler requires DI services beyond the message and CancellationToken.
    /// When false, the interceptor can skip scope creation for maximum performance.
    /// </summary>
    public bool HasMethodDIParameters =>
        Parameters.Any(p => !p.IsMessageParameter && !p.Type.IsCancellationToken);

    /// <summary>
    /// Whether this handler can use the zero-allocation fast path interceptor.
    /// True when the handler is static, has no middleware, no cascading messages, and no method DI parameters.
    /// Static handlers are guaranteed to not have scoped dependencies since they don't use DI for instantiation.
    /// </summary>
    public bool CanUseZeroAllocFastPath =>
        IsStatic && // Must be static for zero-alloc
        !HasMethodDIParameters && // No DI parameters in the method
        !Middleware.Any() && // No middleware - fast path skips middleware execution entirely
        !ReturnType.IsTuple; // No cascading messages

    /// <summary>
    /// Whether this handler can use the singleton fast path interceptor.
    /// True when the handler has no constructor parameters, no DI method parameters, no middleware, and no cascading messages.
    /// This applies to handlers that have parameterless constructors and no DI dependencies.
    /// </summary>
    public bool CanUseSingletonFastPath =>
        !IsStatic && // Instance handler (static uses zero-alloc path)
        !HasConstructorParameters && // No constructor DI parameters
        !HasMethodDIParameters && // No DI parameters in the method
        !Middleware.Any() && // No middleware - fast path skips middleware execution entirely
        !ReturnType.IsTuple; // No cascading messages
}

internal readonly record struct ParameterInfo
{
    public string Name { get; init; }
    public TypeSymbolInfo Type { get; init; }
    public bool IsMessageParameter { get; init; }
}
