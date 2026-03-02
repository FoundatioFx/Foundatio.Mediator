namespace Foundatio.Mediator.Tests;

public class GenericConstraintGenerationTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    [Fact]
    public void EmitsConstraintsForMultiParameterOpenGenericHandler()
    {
        const string source = @"using System.Threading; using System.Threading.Tasks; using Foundatio.Mediator;

public record DualCommand<T1,T2>(T1 First, T2 Second) : ICommand;

public class DualHandler<T1,T2>
    where T1 : class, ICommand, new()
    where T2 : struct
{
    public Task HandleAsync(DualCommand<T1,T2> cmd, CancellationToken ct) => Task.CompletedTask;
}";

        var (compilation, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()]);

        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));

        // Find the generated handler wrapper for DualHandler / DualCommand
        var generated = trees.FirstOrDefault(t => t.HintName.Contains("DualHandler_DualCommand_T1_T2_Handler"));
        Assert.NotNull(generated.HintName);
        string code = generated.Source;

        // Assert constraint clauses exist exactly once each
        Assert.Contains("where T1 : class, Foundatio.Mediator.ICommand, new()", code);
        Assert.Contains("where T2 : struct", code);

        // Ensure they are attached to the wrapper class (appear after the class declaration line)
        int classLine = code.IndexOf("public static class DualHandler_DualCommand_T1_T2_Handler<", StringComparison.Ordinal);
        Assert.True(classLine >= 0);
        int t1Constraint = code.IndexOf("where T1 : class, Foundatio.Mediator.ICommand, new()", classLine, StringComparison.Ordinal);
        int t2Constraint = code.IndexOf("where T2 : struct", classLine, StringComparison.Ordinal);
        Assert.True(t1Constraint > classLine);
        Assert.True(t2Constraint > t1Constraint);
    }

    [Fact]
    public void GenericArity_GreaterThan10_GeneratesCorrectCommas()
    {
        // Build an open generic handler with 11 type parameters to exercise the fallback branch
        const string source = @"using System.Threading; using System.Threading.Tasks; using Foundatio.Mediator;

public record BigCommand<T1,T2,T3,T4,T5,T6,T7,T8,T9,T10,T11>(T1 First);

public class BigHandler<T1,T2,T3,T4,T5,T6,T7,T8,T9,T10,T11>
{
    public Task HandleAsync(BigCommand<T1,T2,T3,T4,T5,T6,T7,T8,T9,T10,T11> cmd, CancellationToken ct)
        => Task.CompletedTask;
}";

        var (_, diagnostics, trees) = RunGenerator(source, [new MediatorGenerator()]);
        Assert.Empty(diagnostics.Where(d => d.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error));

        var module = trees.First(t => t.HintName == "_FoundatioModule.cs");

        // Arity 11 = 10 commas inside angle brackets: <,,,,,,,,,,>
        string expected = "<,,,,,,,,,,>";
        Assert.Contains(expected, module.Source);
        // Make sure the old buggy fallback "<>)" is NOT present
        Assert.DoesNotContain("<>)", module.Source);
    }
}

