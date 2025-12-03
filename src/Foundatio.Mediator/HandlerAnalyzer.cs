using System.Xml.Linq;
using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

internal static class HandlerAnalyzer
{
    public static bool IsMatch(SyntaxNode node)
    {
        if (node is not ClassDeclarationSyntax { Identifier.ValueText: var name } classDecl)
            return false;

        if (name.EndsWith("Handler") || name.EndsWith("Consumer"))
            return true;

        if (classDecl.BaseList is { Types.Count: > 0 })
        {
            foreach (var bt in classDecl.BaseList.Types)
            {
                string? typeName = bt.Type switch
                {
                    SimpleNameSyntax sns => sns.Identifier.ValueText,
                    QualifiedNameSyntax qns => qns.Right.Identifier.ValueText,
                    AliasQualifiedNameSyntax aq => aq.Name.Identifier.ValueText,
                    _ => (bt.Type as IdentifierNameSyntax)?.Identifier.ValueText
                };

                if (typeName == "IHandler")
                    return true;
            }
        }

        if (classDecl.AttributeLists.Count > 0 && classDecl.AttributeLists
                .SelectMany(al => al.Attributes)
                .Any(a => a.Name is IdentifierNameSyntax { Identifier.ValueText: "Handler" }
                    or QualifiedNameSyntax
                {
                    Right.Identifier.ValueText: "Handler"
                }))
        {
            return true;
        }

        foreach (var member in classDecl.Members)
        {
            if (member is not MethodDeclarationSyntax m || m.AttributeLists.Count <= 0)
                continue;

            if (m.AttributeLists.SelectMany(al => al.Attributes)
                .Any(a => a.Name is IdentifierNameSyntax { Identifier.ValueText: "Handler" }
                    or QualifiedNameSyntax
                {
                    Right.Identifier.ValueText: "Handler"
                }))
            {
                return true;
            }
        }

        return false;
    }

    public static List<HandlerInfo> GetHandlers(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        if (semanticModel.GetDeclaredSymbol(classDeclaration) is not { } classSymbol
            || classSymbol.HasIgnoreAttribute(context.SemanticModel.Compilation))
            return [];

        // Exclude generated handler classes in Foundatio.Mediator namespace with names ending in "_Handler"
        if (classSymbol.ContainingNamespace?.ToDisplayString() == "Foundatio.Mediator" &&
            classSymbol.Name.EndsWith("_Handler"))
            return [];

        // Exclude nested classes inside generic types
        if (IsNestedInGenericType(classSymbol))
            return [];

        // Determine if the class should be treated as a handler class
        bool nameMatches = classSymbol.Name.EndsWith("Handler") || classSymbol.Name.EndsWith("Consumer");
        bool implementsMarker = classSymbol.AllInterfaces.Any(i => i.ToDisplayString() == "Foundatio.Mediator.IHandler");
        bool hasClassHandlerAttribute = classSymbol.GetAttributes().Any(attr => attr.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerAttribute);

        // Explicit discovery: IHandler interface or [Handler] attribute on class
        bool isExplicitlyDeclared = implementsMarker || hasClassHandlerAttribute;

        bool treatAsHandlerClass = nameMatches || implementsMarker || hasClassHandlerAttribute;

        var handlerMethods = GetMethods(classSymbol)
            .Where(m => IsHandlerMethod(m, context.SemanticModel.Compilation, treatAsHandlerClass))
            .ToList();

        if (handlerMethods.Count == 0)
            return [];

        var handlers = new List<HandlerInfo>();

        foreach (var handlerMethod in handlerMethods)
        {
            if (handlerMethod.Parameters.Length == 0)
                continue;

            if (handlerMethod.IsGenericMethod)
                continue; // do not support generic handler methods, only generic classes

            var messageParameter = handlerMethod.Parameters[0];
            var messageType = messageParameter.Type;

            var parameterInfos = new List<ParameterInfo>();

            foreach (var parameter in handlerMethod.Parameters)
            {
                bool isMessage = SymbolEqualityComparer.Default.Equals(parameter, messageParameter);

                parameterInfos.Add(new ParameterInfo
                {
                    Name = parameter.Name,
                    Type = TypeSymbolInfo.From(parameter.Type, context.SemanticModel.Compilation),
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
                genericConstraints = classSymbol.TypeParameters.Select(BuildConstraintClause).Where(s => s.Length > 0).ToArray();
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

            // A handler method is explicitly declared if:
            // 1. The class implements IHandler interface (isExplicitlyDeclared includes implementsMarker)
            // 2. The class has [Handler] attribute (isExplicitlyDeclared includes hasClassHandlerAttribute)
            // 3. The method has [Handler] attribute
            bool hasMethodHandlerAttribute = handlerMethod.GetAttributes().Any(attr => attr.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerAttribute);
            bool methodIsExplicitlyDeclared = isExplicitlyDeclared || hasMethodHandlerAttribute;

            // Extract Order and Lifetime from [Handler] attribute (method attribute takes precedence over class attribute)
            int order = int.MaxValue;
            string? lifetime = null;
            var handlerAttr = hasMethodHandlerAttribute
                ? handlerMethod.GetAttributes().FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerAttribute)
                : hasClassHandlerAttribute
                    ? classSymbol.GetAttributes().FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerAttribute)
                    : null;

            if (handlerAttr != null)
            {
                // Check constructor argument for order
                if (handlerAttr.ConstructorArguments.Length > 0 && handlerAttr.ConstructorArguments[0].Value is int orderValue)
                    order = orderValue;

                // Check named argument (property) for Order
                var orderArg = handlerAttr.NamedArguments.FirstOrDefault(na => na.Key == "Order");
                if (orderArg.Value.Value is int namedOrderValue)
                    order = namedOrderValue;

                // Check named argument (property) for Lifetime
                // The enum value is stored as an int in the attribute data
                var lifetimeArg = handlerAttr.NamedArguments.FirstOrDefault(na => na.Key == "Lifetime");
                if (lifetimeArg.Value.Value is int lifetimeValue && lifetimeValue > 0)
                {
                    // Map enum values: 0=Default, 1=Transient, 2=Scoped, 3=Singleton
                    lifetime = lifetimeValue switch
                    {
                        1 => "Transient",
                        2 => "Scoped",
                        3 => "Singleton",
                        _ => null // 0 (Default) means use project-level default
                    };
                }
            }

            // Check if the handler has constructor parameters (indicating DI dependencies)
            bool hasConstructorParameters = !handlerMethod.IsStatic &&
                classSymbol.InstanceConstructors.Any(c => c.Parameters.Length > 0);

            string? messageSummary = GetSummaryComment(messageType);

            handlers.Add(new HandlerInfo
            {
                Identifier = classSymbol.Name.ToIdentifier(),
                FullName = classSymbol.ToDisplayString(),
                MethodName = handlerMethod.Name,
                MessageType = TypeSymbolInfo.From(messageType, context.SemanticModel.Compilation),
                MessageSummary = messageSummary,
                MessageInterfaces = new(messageInterfaces.ToArray()),
                MessageBaseClasses = new(messageBaseClasses.ToArray()),
                ReturnType = TypeSymbolInfo.From(handlerMethod.ReturnType, context.SemanticModel.Compilation),
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
                IsExplicitlyDeclared = methodIsExplicitlyDeclared,
                Order = order,
                Lifetime = lifetime,
                HasConstructorParameters = hasConstructorParameters,
            });
        }

        return handlers;
    }

    private static string? GetSummaryComment(ISymbol symbol)
    {
        var documentation = symbol.GetDocumentationCommentXml(expandIncludes: true);
        if (String.IsNullOrWhiteSpace(documentation))
            return null;

        try
        {
            var document = XDocument.Parse(documentation);
            var summary = document.Root?.Elements("summary").FirstOrDefault();
            if (summary == null)
                return null;

            var text = summary.Value;
            if (String.IsNullOrWhiteSpace(text))
                return null;

            var normalized = String.Join(" ", text
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim())
                .Where(t => t.Length > 0));

            return normalized.Length == 0 ? null : normalized;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsHandlerMethod(IMethodSymbol method, Compilation compilation, bool treatAsHandlerClass)
    {
        if (method.DeclaredAccessibility != Accessibility.Public)
            return false;

        bool hasMethodHandlerAttribute = method.GetAttributes().Any(attr => attr.AttributeClass?.ToDisplayString() == WellKnownTypes.HandlerAttribute);
        if (!treatAsHandlerClass && !hasMethodHandlerAttribute)
            return false;

        if (treatAsHandlerClass && !ValidHandlerMethodNames.Contains(method.Name))
            return false;

        if (method.HasIgnoreAttribute(compilation))
            return false;

        if (method.IsMassTransitConsumeMethod())
            return false;

        return true;
    }

    private static IEnumerable<IMethodSymbol> GetMethods(INamedTypeSymbol targetSymbol, bool includeBaseMethods = true)
    {
        var methods = new Dictionary<string, IMethodSymbol>();

        var currentSymbol = targetSymbol;

        while (currentSymbol != null)
        {
            var methodSymbols = currentSymbol
                .GetMembers()
                .Where(m => m.Kind == SymbolKind.Method)
                .OfType<IMethodSymbol>();

            foreach (var methodSymbol in methodSymbols)
            {
                string signature = BuildMethodSignature(methodSymbol);

                if (!methods.ContainsKey(signature))
                    methods.Add(signature, methodSymbol);
            }

            if (!includeBaseMethods)
                break;

            currentSymbol = currentSymbol.BaseType;
        }

        return methods.Values;
    }

    private static string BuildMethodSignature(IMethodSymbol method)
    {
        if (method.Parameters.Length == 0)
            return method.Name + "()";

        string[] parts = new string[method.Parameters.Length];
        for (int i = 0; i < method.Parameters.Length; i++)
            parts[i] = method.Parameters[i].Type.ToDisplayString();

        return method.Name + "(" + String.Join(",", parts) + ")";
    }

    private static readonly string[] ValidHandlerMethodNames = [
        "Handle", "HandleAsync",
        "Handles", "HandlesAsync",
        "Consume", "ConsumeAsync",
        "Consumes", "ConsumesAsync"
    ];

    private static string BuildConstraintClause(ITypeParameterSymbol tp)
    {
        var ordered = new List<string>();

        if (tp.HasReferenceTypeConstraint)
            ordered.Add("class");
        else if (tp.HasValueTypeConstraint)
            ordered.Add("struct");
        else if (tp.HasUnmanagedTypeConstraint)
            ordered.Add("unmanaged");

        foreach (var c in tp.ConstraintTypes)
        {
            string display = c.ToDisplayString();
            if (!ordered.Contains(display))
                ordered.Add(display);
        }

        if (tp.HasNotNullConstraint)
            ordered.Add("notnull");
        if (tp.HasConstructorConstraint)
            ordered.Add("new()");

        if (ordered.Count == 0)
            return String.Empty;

        return $"where {tp.Name} : {String.Join(", ", ordered)}";
    }

    private static bool IsNestedInGenericType(INamedTypeSymbol typeSymbol)
    {
        var containingType = typeSymbol.ContainingType;
        while (containingType != null)
        {
            if (containingType.IsGenericType)
                return true;
            containingType = containingType.ContainingType;
        }
        return false;
    }
}
