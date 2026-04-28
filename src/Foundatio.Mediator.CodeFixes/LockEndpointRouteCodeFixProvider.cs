using System.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.Formatting;

namespace Foundatio.Mediator;

/// <summary>
/// Code fix provider for FMED017 that offers "Lock this endpoint route" and "Lock all endpoint routes in class"
/// actions. These insert explicit <c>[HandlerEndpoint("route")]</c> and/or <c>[HandlerEndpointGroup("Name")]</c>
/// attributes so that convention-based routes are frozen and won't change across library versions.
/// </summary>
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(LockEndpointRouteCodeFixProvider)), Shared]
public sealed class LockEndpointRouteCodeFixProvider : CodeFixProvider
{
    public override ImmutableArray<string> FixableDiagnosticIds { get; } =
        ImmutableArray.Create("FMED017");

    public override FixAllProvider? GetFixAllProvider() => WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document.GetSyntaxRootAsync(context.CancellationToken).ConfigureAwait(false);
        if (root == null)
            return;

        foreach (var diagnostic in context.Diagnostics)
        {
            // Only offer the fix when the route is convention-derived (not already explicit)
            if (diagnostic.Properties.TryGetValue("HasExplicitRoute", out var hasExplicit) && hasExplicit == "true")
                continue;

            var diagnosticSpan = diagnostic.Location.SourceSpan;
            var node = root.FindToken(diagnosticSpan.Start).Parent;

            // Find the method declaration
            var methodDecl = node?.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (methodDecl == null)
                continue;

            var classDecl = methodDecl.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault();
            if (classDecl == null)
                continue;

            // Register "Make endpoint explicit" for the single method
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Make endpoint explicit",
                    createChangedDocument: ct => LockMethodRouteAsync(context.Document, methodDecl, classDecl, diagnostic, ct),
                    equivalenceKey: "MakeEndpointExplicit"),
                diagnostic);

            // Register "Make all endpoints in class explicit" for the whole class
            context.RegisterCodeFix(
                CodeAction.Create(
                    title: "Make all endpoints in class explicit",
                    createChangedDocument: ct => LockAllRoutesInClassAsync(context.Document, classDecl, context.Diagnostics, ct),
                    equivalenceKey: "MakeAllEndpointsExplicit"),
                diagnostic);
        }
    }

    private static async Task<Document> LockMethodRouteAsync(
        Document document,
        MethodDeclarationSyntax methodDecl,
        ClassDeclarationSyntax classDecl,
        Diagnostic diagnostic,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var newRoot = root;

        // Add [HandlerEndpointGroup] to class if not present
        newRoot = EnsureGroupAttribute(newRoot, classDecl, diagnostic);

        // Re-find method after potential class modification
        methodDecl = newRoot.FindToken(methodDecl.Identifier.SpanStart).Parent?
            .AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault() ?? methodDecl;

        // Add [HandlerEndpoint("route")] to the method
        newRoot = AddEndpointAttribute(newRoot, methodDecl, diagnostic);

        return document.WithSyntaxRoot(newRoot);
    }

    private static async Task<Document> LockAllRoutesInClassAsync(
        Document document,
        ClassDeclarationSyntax classDecl,
        ImmutableArray<Diagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null)
            return document;

        var newRoot = root;

        // Collect all FMED017 diagnostics in this class
        var firstDiag = diagnostics.FirstOrDefault(d =>
            d.Id == "FMED017" &&
            (!d.Properties.TryGetValue("HasExplicitRoute", out var he) || he != "true"));

        if (firstDiag == null)
            return document;

        // Add [HandlerEndpointGroup] to class if not present
        newRoot = EnsureGroupAttribute(newRoot, classDecl, firstDiag);

        // Re-find class after potential modification
        classDecl = newRoot.FindToken(classDecl.Identifier.SpanStart).Parent?
            .AncestorsAndSelf().OfType<ClassDeclarationSyntax>().FirstOrDefault() ?? classDecl;

        // Add [HandlerEndpoint] to each method that has a FMED017 diagnostic without explicit route
        foreach (var diag in diagnostics)
        {
            if (diag.Id != "FMED017")
                continue;
            if (diag.Properties.TryGetValue("HasExplicitRoute", out var he) && he == "true")
                continue;

            var token = newRoot.FindToken(diag.Location.SourceSpan.Start);
            var method = token.Parent?.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
            if (method == null)
                continue;

            newRoot = AddEndpointAttribute(newRoot, method, diag);
        }

        return document.WithSyntaxRoot(newRoot);
    }

    private static SyntaxNode EnsureGroupAttribute(
        SyntaxNode root,
        ClassDeclarationSyntax classDecl,
        Diagnostic diagnostic)
    {
        // Check if [HandlerEndpointGroup] is already present
        if (diagnostic.Properties.TryGetValue("HasGroupAttribute", out var hasGroup) && hasGroup == "true")
            return root;

        diagnostic.Properties.TryGetValue("GroupName", out var groupName);
        if (string.IsNullOrEmpty(groupName))
            return root;

        // Build [HandlerEndpointGroup("GroupName")]
        var attributeArg = SyntaxFactory.AttributeArgument(
            SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(groupName!)));

        var attribute = SyntaxFactory.Attribute(
            SyntaxFactory.IdentifierName("HandlerEndpointGroup"),
            SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SingletonSeparatedList(attributeArg)));

        var attributeList = SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(attribute))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
            .WithAdditionalAnnotations(Formatter.Annotation);

        // Use only the indentation whitespace (not doc comments) from the class declaration
        var lastWhitespace = classDecl.GetLeadingTrivia()
            .LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));
        if (lastWhitespace != default)
            attributeList = attributeList.WithLeadingTrivia(lastWhitespace);

        var newClassDecl = classDecl.AddAttributeLists(attributeList);
        return root.ReplaceNode(classDecl, newClassDecl);
    }

    private static SyntaxNode AddEndpointAttribute(
        SyntaxNode root,
        MethodDeclarationSyntax methodDecl,
        Diagnostic diagnostic)
    {
        // Check if method already has [HandlerEndpoint]
        foreach (var attrList in methodDecl.AttributeLists)
        {
            foreach (var attr in attrList.Attributes)
            {
                var name = attr.Name.ToString();
                if (name is "HandlerEndpoint" or "HandlerEndpointAttribute")
                    return root;
            }
        }

        diagnostic.Properties.TryGetValue("Route", out var route);
        diagnostic.Properties.TryGetValue("HttpMethod", out var httpMethod);
        route ??= "";

        // Build [HandlerEndpoint(HandlerMethod.Get, "/{route}")]
        var args = new List<AttributeArgumentSyntax>();

        if (!string.IsNullOrEmpty(httpMethod))
        {
            // Map "GET" → "Get", "POST" → "Post", etc. to match HandlerMethod enum members
            var enumMember = httpMethod!.Substring(0, 1) + httpMethod.Substring(1).ToLowerInvariant();
            args.Add(SyntaxFactory.AttributeArgument(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("HandlerMethod"),
                    SyntaxFactory.IdentifierName(enumMember))));
        }

        args.Add(SyntaxFactory.AttributeArgument(
            SyntaxFactory.LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                SyntaxFactory.Literal(route))));

        var attribute = SyntaxFactory.Attribute(
            SyntaxFactory.IdentifierName("HandlerEndpoint"),
            SyntaxFactory.AttributeArgumentList(
                SyntaxFactory.SeparatedList(args)));

        var attributeList = SyntaxFactory.AttributeList(
            SyntaxFactory.SingletonSeparatedList(attribute))
            .WithTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed)
            .WithAdditionalAnnotations(Formatter.Annotation);

        // Use only the indentation whitespace (not doc comments) from the method declaration
        var lastWhitespace = methodDecl.GetLeadingTrivia()
            .LastOrDefault(t => t.IsKind(SyntaxKind.WhitespaceTrivia));
        if (lastWhitespace != default)
            attributeList = attributeList.WithLeadingTrivia(lastWhitespace);

        var newMethodDecl = methodDecl.AddAttributeLists(attributeList);
        return root.ReplaceNode(methodDecl, newMethodDecl);
    }
}
