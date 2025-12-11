using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Foundatio.Mediator.Tests;

public class IdentifierGenerationTests : GeneratorTestBase
{
    [Fact]
    public async Task GeneratesValidIdentifierForAssemblyNameStartingWithDigit()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Ping(string Message) : IQuery<string>;

            public class PingHandler {
                public Task<string> HandleAsync(Ping message, CancellationToken ct) => Task.FromResult(message.Message + " Pong");
            }
            """;

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);
        
        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.ServiceCollection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IMediator).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MediatorGenerator).Assembly.Location)
        };
        
        var customCompilation = CSharpCompilation.Create(
            assemblyName: "123ABC",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new MediatorGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [generator.AsSourceGenerator()], 
            additionalTexts: null,
            parseOptions: parseOptions, 
            optionsProvider: null);
        driver = driver.RunGeneratorsAndUpdateCompilation(customCompilation, out var outputCompilation, out var outputDiagnostics);

        var genResult = driver.GetRunResult();
        var generatedSources = genResult.Results
            .SelectMany(r => r.GeneratedSources)
            .ToList();

        // Find the DI registration file which should have the prefixed assembly name
        var diRegistrationFile = generatedSources.FirstOrDefault(s => s.HintName.Contains("MediatorHandlers"));
        Assert.NotNull(diRegistrationFile.HintName);
        
        var sourceText = diRegistrationFile.SourceText.ToString();
        
        // The class name should be _123ABC_MediatorHandlers (prefixed with underscore)
        Assert.Contains("public static class _123ABC_MediatorHandlers", sourceText);
        
        // Verify it's valid C# by checking compilation errors
        var errors = outputDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Assert.Empty(errors);
    }

    [Fact]
    public async Task GeneratesValidIdentifierForHandlerWithMessageTypeStartingWithDigit()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            namespace MyNamespace
            {
                public record _123Message(string Value);

                public class MessageHandler {
                    public Task HandleAsync(_123Message message, CancellationToken ct) => Task.CompletedTask;
                }
            }
            """;

        await VerifyGenerated(source, new MediatorGenerator());
    }
}
