using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

/// <summary>
/// Scans referenced assemblies marked with [FoundatioModule] for handler classes.
/// Uses the same discovery logic as HandlerAnalyzer but via metadata instead of syntax.
/// </summary>
internal static class CrossAssemblyHandlerScanner
{


    /// <summary>
    /// Scans referenced assemblies for handler classes that can be called cross-assembly.
    /// </summary>
    public static List<HandlerInfo> ScanReferencedAssemblies(Compilation compilation)
    {
        var handlers = new List<HandlerInfo>();
        var moduleAttribute = compilation.GetTypeByMetadataName(WellKnownTypes.FoundatioModuleAttribute);
        var handlerAttribute = compilation.GetTypeByMetadataName(WellKnownTypes.HandlerAttribute);
        var handlerInterface = compilation.GetTypeByMetadataName(WellKnownTypes.IHandler);

        if (moduleAttribute == null)
            return handlers;

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

            // Scan all types in the assembly for handlers
            var visitor = new HandlerTypeVisitor(handlerAttribute, handlerInterface, compilation);
            visitor.Visit(assemblySymbol.GlobalNamespace);
            handlers.AddRange(visitor.Handlers);
        }

        return handlers;
    }

    private class HandlerTypeVisitor : SymbolVisitor
    {
        private readonly INamedTypeSymbol? _handlerAttribute;
        private readonly INamedTypeSymbol? _handlerInterface;
        private readonly Compilation _compilation;
        public List<HandlerInfo> Handlers { get; } = new();

        public HandlerTypeVisitor(INamedTypeSymbol? handlerAttribute, INamedTypeSymbol? handlerInterface, Compilation compilation)
        {
            _handlerAttribute = handlerAttribute;
            _handlerInterface = handlerInterface;
            _compilation = compilation;
        }

        public override void VisitNamespace(INamespaceSymbol symbol)
        {
            foreach (var member in symbol.GetMembers())
            {
                member.Accept(this);
            }
        }

        public override void VisitNamedType(INamedTypeSymbol classSymbol)
        {
            // Visit nested types first
            foreach (var member in classSymbol.GetTypeMembers())
            {
                member.Accept(this);
            }

            // Skip if not a class
            if (classSymbol.TypeKind != TypeKind.Class)
                return;

            // Skip abstract classes
            if (classSymbol.IsAbstract)
                return;

            // Skip if has [FoundatioIgnore] attribute
            if (classSymbol.HasIgnoreAttribute(_compilation))
                return;

            // Skip internal or private handlers from cross-assembly usage
            // Only public handlers can be used across assemblies
            if (classSymbol.DeclaredAccessibility != Accessibility.Public)
                return;

            // Exclude generated handler classes in Foundatio.Mediator.Generated namespace with names ending in "_Handler"
            if (classSymbol.ContainingNamespace?.ToDisplayString() == WellKnownTypes.GeneratedNamespace &&
                classSymbol.Name.EndsWith("_Handler"))
                return;

            // Exclude nested classes inside generic types
            if (SymbolUtilities.IsNestedInGenericType(classSymbol))
                return;

            // Determine if the class should be treated as a handler class (same logic as HandlerAnalyzer)
            bool nameMatches = classSymbol.Name.EndsWith("Handler") || classSymbol.Name.EndsWith("Consumer");

            bool implementsMarker = _handlerInterface != null &&
                classSymbol.AllInterfaces.Any(i => SymbolEqualityComparer.Default.Equals(i, _handlerInterface));

            bool hasClassHandlerAttribute = _handlerAttribute != null &&
                classSymbol.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, _handlerAttribute));

            bool treatAsHandlerClass = nameMatches || implementsMarker || hasClassHandlerAttribute;

            // Get handler methods
            var handlerMethods = SymbolUtilities.GetMethods(classSymbol)
                .Where(m => IsHandlerMethod(m, treatAsHandlerClass))
                .ToList();

            if (handlerMethods.Count == 0)
                return;

            // Create HandlerInfo for each handler method
            foreach (var handlerMethod in handlerMethods)
            {
                var handlerInfo = ExtractHandlerInfo(classSymbol, handlerMethod);
                if (handlerInfo != null)
                {
                    Handlers.Add(handlerInfo.Value);
                }
            }
        }

        private HandlerInfo? ExtractHandlerInfo(INamedTypeSymbol classSymbol, IMethodSymbol handlerMethod)
        {
            if (handlerMethod.Parameters.Length == 0)
                return null;

            if (handlerMethod.IsGenericMethod)
                return null; // do not support generic handler methods, only generic classes

            var messageParameter = handlerMethod.Parameters[0];
            var messageType = messageParameter.Type;

            var parameterInfos = new List<ParameterInfo>();

            foreach (var parameter in handlerMethod.Parameters)
            {
                bool isMessage = SymbolEqualityComparer.Default.Equals(parameter, messageParameter);

                parameterInfos.Add(new ParameterInfo
                {
                    Name = parameter.Name,
                    Type = TypeSymbolInfo.From(parameter.Type, _compilation),
                    IsMessageParameter = isMessage
                });
            }

            string? messageGenericDefinition = null;
            int messageGenericArity = 0;
            if (messageType is INamedTypeSymbol namedMsg && namedMsg.IsGenericType)
            {
                messageGenericDefinition = namedMsg.ConstructUnboundGenericType().ToDisplayString();
                messageGenericArity = namedMsg.TypeArguments.Length;
            }

            string[] genericParamNames;
            string[] genericConstraints;
            if (classSymbol.IsGenericType)
            {
                genericParamNames = classSymbol.TypeParameters.Select(tp => tp.Name).ToArray();
                genericConstraints = classSymbol.TypeParameters.Select(SymbolUtilities.BuildConstraintClause).Where(s => s.Length > 0).ToArray();
            }
            else
            {
                genericParamNames = [];
                genericConstraints = [];
            }

            var messageInterfaces = new List<string>();
            var messageBaseClasses = new List<string>();

            if (messageType is INamedTypeSymbol namedMessageType)
            {
                foreach (var iface in namedMessageType.AllInterfaces)
                {
                    messageInterfaces.Add(iface.ToDisplayString());
                }

                var currentBase = namedMessageType.BaseType;
                while (currentBase != null && currentBase.SpecialType != SpecialType.System_Object)
                {
                    messageBaseClasses.Add(currentBase.ToDisplayString());
                    currentBase = currentBase.BaseType;
                }
            }

            // Check if the handler has constructor parameters (indicating DI dependencies)
            bool hasConstructorParameters = !handlerMethod.IsStatic &&
                classSymbol.InstanceConstructors.Any(c => c.Parameters.Length > 0);

            return new HandlerInfo
            {
                Identifier = classSymbol.Name.ToIdentifier(),
                FullName = classSymbol.ToDisplayString(),
                MethodName = handlerMethod.Name,
                MessageType = TypeSymbolInfo.From(messageType, _compilation),
                MessageInterfaces = new(messageInterfaces.ToArray()),
                MessageBaseClasses = new(messageBaseClasses.ToArray()),
                ReturnType = TypeSymbolInfo.From(handlerMethod.ReturnType, _compilation),
                IsStatic = handlerMethod.IsStatic,
                IsGenericHandlerClass = classSymbol.IsGenericType,
                GenericArity = classSymbol.IsGenericType ? classSymbol.TypeParameters.Length : 0,
                GenericTypeParameters = new(genericParamNames),
                MessageGenericTypeDefinitionFullName = messageGenericDefinition,
                MessageGenericArity = messageGenericArity,
                GenericConstraints = new(genericConstraints),
                Parameters = new(parameterInfos.ToArray()),
                CallSites = [],
                Middleware = [],
                HasConstructorParameters = hasConstructorParameters,
                Order = int.MaxValue,
            };
        }

        private bool IsHandlerMethod(IMethodSymbol method, bool treatAsHandlerClass)
        {
            if (method.DeclaredAccessibility != Accessibility.Public)
                return false;

            bool hasMethodHandlerAttribute = _handlerAttribute != null &&
                method.GetAttributes().Any(attr => SymbolEqualityComparer.Default.Equals(attr.AttributeClass, _handlerAttribute));

            if (!treatAsHandlerClass && !hasMethodHandlerAttribute)
                return false;

            if (treatAsHandlerClass && !SymbolUtilities.ValidHandlerMethodNames.Contains(method.Name))
                return false;

            if (method.HasIgnoreAttribute(_compilation))
                return false;

            if (method.IsMassTransitConsumeMethod())
                return false;

            return true;
        }

    }
}
