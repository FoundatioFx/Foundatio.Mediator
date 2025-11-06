using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Foundatio.Mediator;

internal static class MiddlewareAnalyzer
{
    public static bool IsMatch(SyntaxNode node)
    {
        return node is ClassDeclarationSyntax { Identifier.ValueText: var name }
               && name.EndsWith("Middleware");
    }

    public static MiddlewareInfo? GetMiddleware(GeneratorSyntaxContext context)
    {
        var classDeclaration = (ClassDeclarationSyntax)context.Node;
        var semanticModel = context.SemanticModel;

        if (semanticModel.GetDeclaredSymbol(classDeclaration) is not INamedTypeSymbol classSymbol)
            return null;

        if (classSymbol.HasIgnoreAttribute(context.SemanticModel.Compilation))
            return null;

        var beforeMethods = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => IsMiddlewareBeforeMethod(m, context.SemanticModel.Compilation))
            .ToList();

        var afterMethods = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => IsMiddlewareAfterMethod(m, context.SemanticModel.Compilation))
            .ToList();

        var finallyMethods = classSymbol.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(m => IsMiddlewareFinallyMethod(m, context.SemanticModel.Compilation))
            .ToList();

        if (beforeMethods.Count == 0 && afterMethods.Count == 0 && finallyMethods.Count == 0)
            return null;

        var diagnostics = new List<DiagnosticInfo>();

        if (beforeMethods.Count > 1)
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Identifier = "FMED001",
                Title = "Multiple Before Methods in Middleware",
                Message = $"Middleware '{classSymbol.Name}' has multiple Before methods. Only one Before/BeforeAsync method is allowed per middleware class.",
                Severity = DiagnosticSeverity.Error,
                Location = LocationInfo.CreateFrom(classDeclaration)
            });
        }

        if (afterMethods.Count > 1)
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Identifier = "FMED002",
                Title = "Multiple After Methods in Middleware",
                Message = $"Middleware '{classSymbol.Name}' has multiple After methods. Only one After/AfterAsync method is allowed per middleware class.",
                Severity = DiagnosticSeverity.Error,
                Location = LocationInfo.CreateFrom(classDeclaration)
            });
        }

        if (finallyMethods.Count > 1)
        {
            diagnostics.Add(new DiagnosticInfo
            {
                Identifier = "FMED003",
                Title = "Multiple Finally Methods in Middleware",
                Message = $"Middleware '{classSymbol.Name}' has multiple Finally methods. Only one Finally/FinallyAsync method is allowed per middleware class.",
                Severity = DiagnosticSeverity.Error,
                Location = LocationInfo.CreateFrom(classDeclaration)
            });
        }

        var beforeMethod = beforeMethods.FirstOrDefault();
        var afterMethod = afterMethods.FirstOrDefault();
        var finallyMethod = finallyMethods.FirstOrDefault();

        var allMethods = new[] { beforeMethod, afterMethod, finallyMethod }.Where(m => m != null).ToList();

        // Validate mixed static and instance methods
        if (allMethods.Any() && !classSymbol.IsStatic)
        {
            var staticMethods = allMethods.Where(m => m!.IsStatic).ToList();
            var instanceMethods = allMethods.Where(m => !m!.IsStatic).ToList();

            if (staticMethods.Any() && instanceMethods.Any())
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Identifier = "FMED004",
                    Title = "Mixed Static and Instance Middleware Methods",
                    Message = $"Middleware '{classSymbol.Name}' has both static and instance methods. All middleware methods must be either static or instance methods consistently.",
                    Severity = DiagnosticSeverity.Error,
                    Location = LocationInfo.CreateFrom(classDeclaration)
                });
            }
        }

        // Validate all message types are the same
        if (allMethods.Count > 1)
        {
            var messageTypes = allMethods
                .Where(m => m!.Parameters.Length > 0)
                .Select(m => m!.Parameters[0].Type)
                .Distinct(SymbolEqualityComparer.Default)
                .ToList();

            if (messageTypes.Count > 1)
            {
                diagnostics.Add(new DiagnosticInfo
                {
                    Identifier = "FMED005",
                    Title = "Middleware Message Type Mismatch",
                    Message = $"Middleware '{classSymbol.Name}' handles different message types. All middleware methods in the same class must handle the same message type.",
                    Severity = DiagnosticSeverity.Error,
                    Location = LocationInfo.CreateFrom(classDeclaration)
                });
            }
        }

        ITypeSymbol? messageType = beforeMethod?.Parameters[0].Type
            ?? afterMethod?.Parameters[0].Type
            ?? finallyMethod?.Parameters[0].Type;

        bool isStatic = classSymbol.IsStatic
                        || beforeMethod is { IsStatic: true }
                        || afterMethod is { IsStatic: true }
                        || finallyMethod is { IsStatic: true };

        if (messageType == null)
            return null;

        int? order = null;

        // First check [Middleware(order)] attribute
        var middlewareAttr = classSymbol.GetAttributes()
            .FirstOrDefault(attr => attr.AttributeClass?.ToDisplayString() == WellKnownTypes.MiddlewareAttribute);

        if (middlewareAttr != null)
        {
            // Check constructor argument
            if (middlewareAttr.ConstructorArguments.Length > 0)
            {
                var arg = middlewareAttr.ConstructorArguments[0];
                if (arg.Value is int orderValue)
                    order = orderValue;
            }

            // Check named argument (property)
            var orderArg = middlewareAttr.NamedArguments.FirstOrDefault(na => na.Key == "Order");
            if (orderArg.Value.Value is int namedOrderValue)
                order = namedOrderValue;
        }

        return new MiddlewareInfo
        {
            Identifier = classSymbol.Name.ToIdentifier(),
            FullName = classSymbol.ToDisplayString(),
            MessageType = TypeSymbolInfo.From(messageType, context.SemanticModel.Compilation),
            BeforeMethod = beforeMethod != null ? CreateMiddlewareMethodInfo(beforeMethod, context.SemanticModel.Compilation) : null,
            AfterMethod = afterMethod != null ? CreateMiddlewareMethodInfo(afterMethod, context.SemanticModel.Compilation) : null,
            FinallyMethod = finallyMethod != null ? CreateMiddlewareMethodInfo(finallyMethod, context.SemanticModel.Compilation) : null,
            IsStatic = isStatic,
            Order = order,
            Diagnostics = new(diagnostics.ToArray()),
        };
    }

    private static MiddlewareMethodInfo CreateMiddlewareMethodInfo(IMethodSymbol method, Compilation compilation)
    {
        var parameterInfos = new List<ParameterInfo>();

        foreach (var parameter in method.Parameters)
        {
            bool isMessage = SymbolEqualityComparer.Default.Equals(parameter, method.Parameters[0]);

            parameterInfos.Add(new ParameterInfo
            {
                Name = parameter.Name,
                Type = TypeSymbolInfo.From(parameter.Type, compilation),
                IsMessageParameter = isMessage
            });
        }

        return new MiddlewareMethodInfo
        {
            MethodName = method.Name,
            MessageType = TypeSymbolInfo.From(method.Parameters[0].Type, compilation),
            ReturnType = TypeSymbolInfo.From(method.ReturnType, compilation),
            IsStatic = method.IsStatic,
            Parameters = new(parameterInfos.ToArray())
        };
    }

    private static bool IsMiddlewareBeforeMethod(IMethodSymbol method, Compilation compilation)
    {
        return MiddlewareBeforeMethodNames.Contains(method.Name) &&
               method.DeclaredAccessibility == Accessibility.Public &&
               !method.HasIgnoreAttribute(compilation);
    }

    private static bool IsMiddlewareAfterMethod(IMethodSymbol method, Compilation compilation)
    {
        return MiddlewareAfterMethodNames.Contains(method.Name) &&
               method.DeclaredAccessibility == Accessibility.Public &&
               !method.HasIgnoreAttribute(compilation);
    }

    private static bool IsMiddlewareFinallyMethod(IMethodSymbol method, Compilation compilation)
    {
        return MiddlewareFinallyMethodNames.Contains(method.Name) &&
               method.DeclaredAccessibility == Accessibility.Public &&
               !method.HasIgnoreAttribute(compilation);
    }

    private static readonly string[] MiddlewareBeforeMethodNames = [ "Before", "BeforeAsync" ];
    private static readonly string[] MiddlewareAfterMethodNames = [ "After", "AfterAsync" ];
    private static readonly string[] MiddlewareFinallyMethodNames = [ "Finally", "FinallyAsync" ];
}
