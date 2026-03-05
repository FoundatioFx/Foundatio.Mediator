using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Foundatio.Mediator.Tests;

public class IdentifierGenerationTests(ITestOutputHelper output) : GeneratorTestBase(output)
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
        var compilation = CreateCompilation(source, parseOptions, assemblyName: "123ABC");

        var generator = new MediatorGenerator();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [generator.AsSourceGenerator()],
            additionalTexts: null,
            parseOptions: parseOptions,
            optionsProvider: null);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var outputDiagnostics, TestCancellationToken);

        var genResult = driver.GetRunResult();
        var generatedSources = genResult.Results
            .SelectMany(r => r.GeneratedSources)
            .ToList();

        // Find the FoundatioModule file which should have the prefixed assembly name in the class
        var foundatioModuleFile = generatedSources.FirstOrDefault(s => s.HintName == "_FoundatioModule.cs");
        Assert.NotNull(foundatioModuleFile.HintName);

        var sourceText = foundatioModuleFile.SourceText.ToString();

        // The class name should be _123ABC_MediatorHandlers (prefixed with underscore)
        Assert.Contains("public static class _123ABC_MediatorHandlers", sourceText);

        // Verify generated code compiles cleanly
        AssertNoCompilationDiagnostics(outputCompilation);
    }
}
