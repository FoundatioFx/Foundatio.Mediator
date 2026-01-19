using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

internal static class MetadataMiddlewareScanner
{
    /// <summary>
    /// Scans referenced assemblies marked with [FoundatioModule] for middleware types.
    /// </summary>
    public static List<MiddlewareInfo> ScanReferencedAssemblies(Compilation compilation)
    {
        var middlewares = new List<MiddlewareInfo>();
        var middlewareAttribute = compilation.GetTypeByMetadataName(WellKnownTypes.MiddlewareAttribute);
        var moduleAttribute = compilation.GetTypeByMetadataName(WellKnownTypes.FoundatioModuleAttribute);

        if (middlewareAttribute == null || moduleAttribute == null)
            return middlewares;

        // Get all referenced assemblies
        foreach (var reference in compilation.References)
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is not IAssemblySymbol assemblySymbol)
                continue;

            // Skip the current assembly (we handle that via syntax analysis)
            if (SymbolEqualityComparer.Default.Equals(assemblySymbol, compilation.Assembly))
                continue;

            // Only scan assemblies marked with [assembly: FoundatioModule]
            bool hasModuleAttribute = assemblySymbol.GetAttributes()
                .Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, moduleAttribute));

            if (!hasModuleAttribute)
                continue;

            // Scan all types in the assembly for middleware
            var visitor = new MiddlewareTypeVisitor(middlewareAttribute, compilation);
            visitor.Visit(assemblySymbol.GlobalNamespace);
            middlewares.AddRange(visitor.Middlewares);
        }

        return middlewares;
    }

    private class MiddlewareTypeVisitor : SymbolVisitor
    {
        private readonly INamedTypeSymbol _middlewareAttribute;
        private readonly Compilation _compilation;
        public List<MiddlewareInfo> Middlewares { get; } = new();

        public MiddlewareTypeVisitor(INamedTypeSymbol middlewareAttribute, Compilation compilation)
        {
            _middlewareAttribute = middlewareAttribute;
            _compilation = compilation;
        }

        public override void VisitNamespace(INamespaceSymbol symbol)
        {
            foreach (var member in symbol.GetMembers())
            {
                member.Accept(this);
            }
        }

        public override void VisitNamedType(INamedTypeSymbol symbol)
        {
            // Check if type has [Middleware] attribute or ends with "Middleware"
            bool hasAttribute = symbol.GetAttributes()
                .Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, _middlewareAttribute));

            bool endsWithMiddleware = symbol.Name.EndsWith("Middleware");

            if (!hasAttribute && !endsWithMiddleware)
                return;

            // Skip if has [FoundatioIgnore] attribute
            if (symbol.HasIgnoreAttribute(_compilation))
                return;

            // Skip internal or private middleware from cross-assembly usage
            // Only public middleware can be used across assemblies
            if (symbol.DeclaredAccessibility != Accessibility.Public)
                return;

            // Try to extract middleware info from metadata
            var middlewareInfo = ExtractMiddlewareInfo(symbol);
            if (middlewareInfo != null)
            {
                Middlewares.Add(middlewareInfo.Value);
            }

            // Visit nested types
            foreach (var member in symbol.GetTypeMembers())
            {
                member.Accept(this);
            }
        }

        private MiddlewareInfo? ExtractMiddlewareInfo(INamedTypeSymbol classSymbol)
        {
            var beforeMethods = classSymbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => IsMiddlewareBeforeMethod(m))
                .ToList();

            var afterMethods = classSymbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => IsMiddlewareAfterMethod(m))
                .ToList();

            var finallyMethods = classSymbol.GetMembers()
                .OfType<IMethodSymbol>()
                .Where(m => IsMiddlewareFinallyMethod(m))
                .ToList();

            if (beforeMethods.Count == 0 && afterMethods.Count == 0 && finallyMethods.Count == 0)
                return null;

            // For now, take the first of each type (validation can happen later)
            var beforeMethod = beforeMethods.FirstOrDefault();
            var afterMethod = afterMethods.FirstOrDefault();
            var finallyMethod = finallyMethods.FirstOrDefault();

            // Get the order from the [Middleware] attribute
            int order = 0;
            var middlewareAttr = classSymbol.GetAttributes()
                .FirstOrDefault(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, _middlewareAttribute));

            if (middlewareAttr != null)
            {
                // Check constructor argument first (e.g., [Middleware(10)])
                if (middlewareAttr.ConstructorArguments.Length > 0 && middlewareAttr.ConstructorArguments[0].Value is int constructorOrder)
                {
                    order = constructorOrder;
                }
                // Then check named argument (e.g., [Middleware(Order = 10)])
                else
                {
                    var orderArg = middlewareAttr.NamedArguments.FirstOrDefault(kvp => kvp.Key == "Order");
                    if (orderArg.Value.Value is int namedOrder)
                        order = namedOrder;
                }
            }

            // Determine message type from first method found
            IMethodSymbol? firstMethod = beforeMethod ?? afterMethod ?? finallyMethod;
            if (firstMethod == null || firstMethod.Parameters.Length == 0)
                return null;

            var messageType = TypeSymbolInfo.From(firstMethod.Parameters[0].Type, _compilation);

            // Determine if middleware is static (class or all methods are static)
            bool isStatic = classSymbol.IsStatic ||
                            (beforeMethod?.IsStatic ?? true) &&
                            (afterMethod?.IsStatic ?? true) &&
                            (finallyMethod?.IsStatic ?? true);

            // Detect constructor parameters (for non-static middleware)
            bool hasConstructorParameters = !isStatic && classSymbol.InstanceConstructors
                .Any(c => c.Parameters.Length > 0);

            // Detect method DI parameters
            bool hasMethodDIParameters = HasMethodDIParameters(beforeMethod, afterMethod, finallyMethod);

            return new MiddlewareInfo
            {
                Identifier = classSymbol.Name,
                FullName = classSymbol.ToDisplayString(),
                MessageType = messageType,
                IsStatic = isStatic,
                Order = order,
                BeforeMethod = beforeMethod != null ? CreateMiddlewareMethodInfo(beforeMethod) : null,
                AfterMethod = afterMethod != null ? CreateMiddlewareMethodInfo(afterMethod) : null,
                FinallyMethod = finallyMethod != null ? CreateMiddlewareMethodInfo(finallyMethod) : null,
                DeclaredAccessibility = classSymbol.DeclaredAccessibility,
                AssemblyName = classSymbol.ContainingAssembly.Name,
                HasConstructorParameters = hasConstructorParameters,
                HasMethodDIParameters = hasMethodDIParameters,
                Diagnostics = new EquatableArray<DiagnosticInfo>([]) // No diagnostics for metadata-based
            };
        }

        private MiddlewareMethodInfo CreateMiddlewareMethodInfo(IMethodSymbol method)
        {
            var parameterInfos = new List<ParameterInfo>();

            foreach (var parameter in method.Parameters)
            {
                bool isMessage = SymbolEqualityComparer.Default.Equals(parameter, method.Parameters[0]);

                parameterInfos.Add(new ParameterInfo
                {
                    Name = parameter.Name,
                    Type = TypeSymbolInfo.From(parameter.Type, _compilation),
                    IsMessageParameter = isMessage
                });
            }

            return new MiddlewareMethodInfo
            {
                MethodName = method.Name,
                MessageType = TypeSymbolInfo.From(method.Parameters[0].Type, _compilation),
                ReturnType = TypeSymbolInfo.From(method.ReturnType, _compilation),
                IsStatic = method.IsStatic,
                Parameters = new(parameterInfos.ToArray())
            };
        }

        private static readonly string[] MiddlewareBeforeMethodNames = ["Before", "BeforeAsync"];
        private static readonly string[] MiddlewareAfterMethodNames = ["After", "AfterAsync"];
        private static readonly string[] MiddlewareFinallyMethodNames = ["Finally", "FinallyAsync"];

        private bool IsMiddlewareBeforeMethod(IMethodSymbol method)
        {
            return MiddlewareBeforeMethodNames.Contains(method.Name) &&
                   method.DeclaredAccessibility == Accessibility.Public &&
                   !method.HasIgnoreAttribute(_compilation) &&
                   method.Parameters.Length > 0;
        }

        private bool IsMiddlewareAfterMethod(IMethodSymbol method)
        {
            return MiddlewareAfterMethodNames.Contains(method.Name) &&
                   method.DeclaredAccessibility == Accessibility.Public &&
                   !method.HasIgnoreAttribute(_compilation) &&
                   method.Parameters.Length > 0;
        }

        private bool IsMiddlewareFinallyMethod(IMethodSymbol method)
        {
            return MiddlewareFinallyMethodNames.Contains(method.Name) &&
                   method.DeclaredAccessibility == Accessibility.Public &&
                   !method.HasIgnoreAttribute(_compilation) &&
                   method.Parameters.Length > 0;
        }

        /// <summary>
        /// Checks if any middleware method has DI parameters that would require serviceProvider.GetRequiredService.
        /// Parameters that are NOT DI parameters: message (first param), CancellationToken, HandlerExecutionInfo, Exception,
        /// and the return type of the Before method (which is passed to After/Finally methods).
        /// </summary>
        private bool HasMethodDIParameters(IMethodSymbol? beforeMethod, IMethodSymbol? afterMethod, IMethodSymbol? finallyMethod)
        {
            // Get the Before method's return type (if any) to exclude from DI detection in After/Finally
            ITypeSymbol? beforeMethodReturnType = null;
            if (beforeMethod != null && !beforeMethod.ReturnsVoid)
            {
                beforeMethodReturnType = beforeMethod.ReturnType;
                // Unwrap Task<T> or ValueTask<T> to get T
                if (beforeMethodReturnType is INamedTypeSymbol { IsGenericType: true } taskType &&
                    (taskType.ConstructedFrom.ToDisplayString() == "System.Threading.Tasks.Task<TResult>" ||
                     taskType.ConstructedFrom.ToDisplayString() == "System.Threading.Tasks.ValueTask<TResult>"))
                {
                    beforeMethodReturnType = taskType.TypeArguments[0];
                }
            }

            return HasDIParameters(beforeMethod, beforeMethodReturnType: null) ||
                   HasDIParameters(afterMethod, beforeMethodReturnType) ||
                   HasDIParameters(finallyMethod, beforeMethodReturnType);
        }

        private bool HasDIParameters(IMethodSymbol? method, ITypeSymbol? beforeMethodReturnType)
        {
            if (method == null)
                return false;

            var exceptionType = _compilation.GetTypeByMetadataName("System.Exception");
            var cancellationTokenType = _compilation.GetTypeByMetadataName("System.Threading.CancellationToken");
            var handlerExecutionInfoType = _compilation.GetTypeByMetadataName(WellKnownTypes.HandlerExecutionInfo);
            var objectType = _compilation.GetSpecialType(SpecialType.System_Object);

            foreach (var param in method.Parameters)
            {
                // First parameter is always the message
                if (SymbolEqualityComparer.Default.Equals(param, method.Parameters[0]))
                    continue;

                // CancellationToken is not a DI parameter
                if (SymbolEqualityComparer.Default.Equals(param.Type, cancellationTokenType))
                    continue;

                // HandlerExecutionInfo is not a DI parameter
                if (SymbolEqualityComparer.Default.Equals(param.Type, handlerExecutionInfoType))
                    continue;

                // Exception (including nullable Exception?) is not a DI parameter
                var unwrappedType = param.Type;
                if (param.Type is INamedTypeSymbol { IsGenericType: true } nullable &&
                    nullable.ConstructedFrom.SpecialType == SpecialType.System_Nullable_T)
                {
                    unwrappedType = nullable.TypeArguments[0];
                }
                if (exceptionType != null && IsExceptionType(unwrappedType, exceptionType))
                    continue;

                // Object type with name "handlerResult" is not a DI parameter
                if (SymbolEqualityComparer.Default.Equals(param.Type, objectType) && param.Name == "handlerResult")
                    continue;

                // Before method return type (including nullable) is not a DI parameter
                // This handles cases like Finally(message, Stopwatch? stopwatch) where Stopwatch is from Before()
                if (beforeMethodReturnType != null &&
                    (SymbolEqualityComparer.Default.Equals(param.Type, beforeMethodReturnType) ||
                     SymbolEqualityComparer.Default.Equals(unwrappedType, beforeMethodReturnType)))
                    continue;

                // Any other parameter type is considered a DI parameter
                return true;
            }

            return false;
        }

        private static bool IsExceptionType(ITypeSymbol type, INamedTypeSymbol exceptionType)
        {
            var current = type;
            while (current != null)
            {
                if (SymbolEqualityComparer.Default.Equals(current, exceptionType))
                    return true;
                current = current.BaseType;
            }
            return false;
        }
    }
}
