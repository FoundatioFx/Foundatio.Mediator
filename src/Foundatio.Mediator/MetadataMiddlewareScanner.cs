using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;
using Microsoft.CodeAnalysis;

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

            return new MiddlewareInfo
            {
                Identifier = classSymbol.Name,
                FullName = classSymbol.ToDisplayString(),
                MessageType = messageType,
                IsStatic = classSymbol.IsStatic,
                Order = order,
                BeforeMethod = beforeMethod != null ? CreateMiddlewareMethodInfo(beforeMethod) : null,
                AfterMethod = afterMethod != null ? CreateMiddlewareMethodInfo(afterMethod) : null,
                FinallyMethod = finallyMethod != null ? CreateMiddlewareMethodInfo(finallyMethod) : null,
                DeclaredAccessibility = classSymbol.DeclaredAccessibility,
                AssemblyName = classSymbol.ContainingAssembly.Name,
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
    }
}
