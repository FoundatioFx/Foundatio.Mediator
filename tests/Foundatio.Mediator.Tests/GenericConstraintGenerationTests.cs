namespace Foundatio.Mediator.Tests;

public class GenericConstraintGenerationTests : GeneratorTestBase {
    [Fact]
    public void EmitsConstraintsForMultiParameterOpenGenericHandler() {
        const string source = @"using System.Threading; using System.Threading.Tasks; using Foundatio.Mediator;

public record DualCommand<T1,T2>(T1 First, T2 Second) : ICommand;

public class DualHandler<T1,T2>
    where T1 : class, ICommand, new()
    where T2 : struct
{
    public Task HandleAsync(DualCommand<T1,T2> cmd, CancellationToken ct) => Task.CompletedTask;
}";

        var (diagnostics, genDiagnostics, trees) = RunGenerator(source, [ new MediatorGenerator() ]);

        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));
        Assert.Empty(genDiagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));

        // Find the generated handler wrapper for DualHandler / DualCommand
        var generated = trees.FirstOrDefault(t => t.HintName.Contains("DualHandler_DualCommand_T1_T2_Handler"));
        Assert.NotNull(generated.HintName);
        string code = generated.Source;

        // Assert constraint clauses exist exactly once each
        Assert.Contains("where T1 : class, Foundatio.Mediator.ICommand, new()", code);
        Assert.Contains("where T2 : struct", code);

        // Ensure they are attached to the wrapper class (appear after the class declaration line)
        int classLine = code.IndexOf("internal static class DualHandler_DualCommand_T1_T2_Handler<", StringComparison.Ordinal);
        Assert.True(classLine >= 0);
        int t1Constraint = code.IndexOf("where T1 : class, Foundatio.Mediator.ICommand, new()", classLine, StringComparison.Ordinal);
        int t2Constraint = code.IndexOf("where T2 : struct", classLine, StringComparison.Ordinal);
        Assert.True(t1Constraint > classLine);
        Assert.True(t2Constraint > t1Constraint);
    }
}
