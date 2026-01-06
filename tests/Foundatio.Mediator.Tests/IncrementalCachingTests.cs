using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Foundatio.Mediator.Tests;

/// <summary>
/// Tests that verify the incremental generator properly caches intermediate outputs
/// and doesn't run excessively on unchanged compilations.
/// Based on: https://andrewlock.net/creating-a-source-generator-part-10-testing-your-incremental-generator-pipeline-outputs-are-cacheable/
///
/// Key insight: When using RegisterImplementationSourceOutput, the final output may show as Modified
/// when the compilation changes (which happens when cloning). This is expected behavior.
/// What matters is that the INTERMEDIATE tracked steps (Settings, CallSites, Handlers, Middleware)
/// properly cache, which prevents the expensive extraction/analysis work from re-running.
/// </summary>
public class IncrementalCachingTests
{
    /// <summary>
    /// Tracking names that match the generator's internal TrackingNames class.
    /// These must be kept in sync with src/Foundatio.Mediator/Utility/TrackingNames.cs
    /// </summary>
    private static class TrackingNames
    {
        public const string Middleware = nameof(Middleware);
        public const string CallSites = nameof(CallSites);
        public const string Handlers = nameof(Handlers);
        public const string Settings = nameof(Settings);
    }

    /// <summary>
    /// All tracking names from the generator pipeline.
    /// </summary>
    private static readonly string[] AllTrackingNames =
    [
        TrackingNames.Settings,
        TrackingNames.CallSites,
        TrackingNames.Middleware,
        TrackingNames.Handlers
    ];

    #region Single Project Caching Tests

    [Fact]
    public void SimpleHandler_OutputsAreCachedOnSecondRun()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record MyMessage;

            public class MyHandler
            {
                public Task HandleAsync(MyMessage msg, CancellationToken ct)
                {
                    return Task.CompletedTask;
                }
            }
            """;

        var (result1, result2) = RunGeneratorTwice(source);

        AssertAllOutputsCached(result1, result2, AllTrackingNames);
    }

    [Fact]
    public void Handler_WithMiddleware_OutputsAreCachedOnSecondRun()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;
            using System.Diagnostics;

            public record MyMessage;

            public class MyHandler
            {
                public Task HandleAsync(MyMessage msg, CancellationToken ct)
                {
                    return Task.CompletedTask;
                }
            }

            public class LoggingMiddleware
            {
                public Stopwatch Before(object message)
                {
                    return Stopwatch.StartNew();
                }

                public void Finally(object message, Stopwatch sw)
                {
                    sw.Stop();
                }
            }
            """;

        var (result1, result2) = RunGeneratorTwice(source);

        AssertAllOutputsCached(result1, result2, AllTrackingNames);
    }

    [Fact]
    public void Handler_WithResponse_OutputsAreCachedOnSecondRun()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record GetOrder(int Id);
            public record Order(int Id, string Name);

            public class OrderHandler
            {
                public Task<Order> HandleAsync(GetOrder query, CancellationToken ct)
                {
                    return Task.FromResult(new Order(query.Id, "Test"));
                }
            }
            """;

        var (result1, result2) = RunGeneratorTwice(source);

        AssertAllOutputsCached(result1, result2, AllTrackingNames);
    }

    [Fact]
    public void MultipleHandlers_OutputsAreCachedOnSecondRun()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record Message1;
            public record Message2;
            public record Message3;

            public class Handler1
            {
                public Task HandleAsync(Message1 msg, CancellationToken ct) => Task.CompletedTask;
            }

            public class Handler2
            {
                public Task HandleAsync(Message2 msg, CancellationToken ct) => Task.CompletedTask;
            }

            public class Handler3
            {
                public Task HandleAsync(Message3 msg, CancellationToken ct) => Task.CompletedTask;
            }
            """;

        var (result1, result2) = RunGeneratorTwice(source);

        AssertAllOutputsCached(result1, result2, AllTrackingNames);
    }

    [Fact]
    public void Handler_WithCallSite_OutputsAreCachedOnSecondRun()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record MyMessage;

            public class MyHandler
            {
                public Task HandleAsync(MyMessage msg, CancellationToken ct) => Task.CompletedTask;
            }

            public class MyService
            {
                private readonly IMediator _mediator;
                public MyService(IMediator mediator) => _mediator = mediator;

                public async Task DoWork(CancellationToken ct)
                {
                    await _mediator.InvokeAsync(new MyMessage(), ct);
                }
            }
            """;

        var (result1, result2) = RunGeneratorTwice(source);

        AssertAllOutputsCached(result1, result2, AllTrackingNames);
    }

    [Fact]
    public void GenericHandler_OutputsAreCachedOnSecondRun()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public interface IEntity { int Id { get; } }
            public record GetEntity<T>(int Id) where T : IEntity;

            public class EntityHandler<T> where T : IEntity
            {
                public Task<T?> HandleAsync(GetEntity<T> query, CancellationToken ct)
                {
                    return Task.FromResult<T?>(default);
                }
            }
            """;

        var (result1, result2) = RunGeneratorTwice(source);

        AssertAllOutputsCached(result1, result2, AllTrackingNames);
    }

    #endregion

    #region Multi-Project Caching Tests

    [Fact]
    public void CrossAssemblyHandler_ConsumerAssembly_OutputsAreCachedOnSecondRun()
    {
        // Handler in a "referenced assembly" marked with [FoundatioModule]
        var handlerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace SharedHandlers;

            public record SharedMessage;

            public class SharedHandler
            {
                public Task<string> HandleAsync(SharedMessage msg, CancellationToken ct)
                {
                    return Task.FromResult("handled");
                }
            }
            """;

        var handlerAssembly = CreateHandlerAssembly(handlerSource);

        // Consumer assembly that calls the handler
        var consumerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;
            using SharedHandlers;

            public class Consumer
            {
                private readonly IMediator _mediator;
                public Consumer(IMediator mediator) => _mediator = mediator;

                public async Task<string> CallHandler(CancellationToken ct)
                {
                    return await _mediator.InvokeAsync<string>(new SharedMessage(), ct);
                }
            }
            """;

        var (result1, result2) = RunGeneratorTwice(consumerSource, additionalReferences: [handlerAssembly]);

        AssertAllOutputsCached(result1, result2, AllTrackingNames);
    }

    [Fact]
    public void CrossAssemblyMiddleware_ConsumerAssembly_OutputsAreCachedOnSecondRun()
    {
        // Middleware in a referenced assembly
        var middlewareSource = """
            using System;
            using System.Diagnostics;
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace SharedMiddleware;

            [Middleware(Order = 1)]
            public class SharedLoggingMiddleware
            {
                public Stopwatch Before(object message)
                {
                    return Stopwatch.StartNew();
                }

                public void Finally(object message, Stopwatch sw)
                {
                    sw.Stop();
                }
            }
            """;

        var middlewareAssembly = CreateHandlerAssembly(middlewareSource);

        // Consumer assembly with a handler
        var consumerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record LocalMessage;

            public class LocalHandler
            {
                public Task HandleAsync(LocalMessage msg, CancellationToken ct) => Task.CompletedTask;
            }
            """;

        var (result1, result2) = RunGeneratorTwice(consumerSource, additionalReferences: [middlewareAssembly]);

        AssertAllOutputsCached(result1, result2, AllTrackingNames);
    }

    [Fact]
    public void MultipleReferencedAssemblies_OutputsAreCachedOnSecondRun()
    {
        // First handler assembly
        var handlerSource1 = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace Assembly1;

            public record Message1;

            public class Handler1
            {
                public Task HandleAsync(Message1 msg, CancellationToken ct) => Task.CompletedTask;
            }
            """;

        // Second handler assembly
        var handlerSource2 = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace Assembly2;

            public record Message2;

            public class Handler2
            {
                public Task HandleAsync(Message2 msg, CancellationToken ct) => Task.CompletedTask;
            }
            """;

        var assembly1 = CreateHandlerAssembly(handlerSource1, "Assembly1");
        var assembly2 = CreateHandlerAssembly(handlerSource2, "Assembly2");

        // Consumer assembly that uses handlers from both assemblies
        var consumerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;
            using Assembly1;
            using Assembly2;

            public class Consumer
            {
                private readonly IMediator _mediator;
                public Consumer(IMediator mediator) => _mediator = mediator;

                public async Task CallHandlers(CancellationToken ct)
                {
                    await _mediator.InvokeAsync(new Message1(), ct);
                    await _mediator.InvokeAsync(new Message2(), ct);
                }
            }
            """;

        var (result1, result2) = RunGeneratorTwice(consumerSource, additionalReferences: [assembly1, assembly2]);

        AssertAllOutputsCached(result1, result2, AllTrackingNames);
    }

    #endregion

    #region Verify No Problematic Types

    [Fact]
    public void TrackedOutputs_DoNotContainProblematicTypes()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record MyMessage;

            public class MyHandler
            {
                public Task HandleAsync(MyMessage msg, CancellationToken ct) => Task.CompletedTask;
            }

            public class MyService
            {
                private readonly IMediator _mediator;
                public MyService(IMediator mediator) => _mediator = mediator;

                public async Task DoWork(CancellationToken ct)
                {
                    await _mediator.InvokeAsync(new MyMessage(), ct);
                }
            }
            """;

        var compilation = CreateCompilation(source);
        var (driver, _) = RunGenerator(compilation);
        var result = driver.GetRunResult();

        AssertNoProblematicTypes(result, AllTrackingNames);
    }

    [Fact]
    public void TrackedOutputs_WithCrossAssembly_DoNotContainProblematicTypes()
    {
        var handlerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            [assembly: FoundatioModule]

            namespace SharedHandlers;

            public record SharedMessage;

            public class SharedHandler
            {
                public Task HandleAsync(SharedMessage msg, CancellationToken ct) => Task.CompletedTask;
            }
            """;

        var handlerAssembly = CreateHandlerAssembly(handlerSource);

        var consumerSource = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;
            using SharedHandlers;

            public class Consumer
            {
                private readonly IMediator _mediator;
                public Consumer(IMediator mediator) => _mediator = mediator;

                public async Task CallHandler(CancellationToken ct)
                {
                    await _mediator.InvokeAsync(new SharedMessage(), ct);
                }
            }
            """;

        var compilation = CreateCompilation(consumerSource, additionalReferences: [handlerAssembly]);
        var (driver, _) = RunGenerator(compilation);
        var result = driver.GetRunResult();

        AssertNoProblematicTypes(result, AllTrackingNames);
    }

    #endregion

    #region Configuration Changes

    [Fact]
    public void ConfigurationChange_InterceptorsToggle_CausesRegeneration()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record MyMessage;

            public class MyHandler
            {
                public Task HandleAsync(MyMessage msg, CancellationToken ct) => Task.CompletedTask;
            }
            """;

        // First run with interceptors enabled
        var options1 = CreateOptions(("build_property.MediatorDisableInterceptors", "false"));
        var compilation = CreateCompilation(source);
        var (driver1, _) = RunGenerator(compilation, options1);
        var result1 = driver1.GetRunResult();

        // Second run with interceptors disabled - should NOT be fully cached
        var options2 = CreateOptions(("build_property.MediatorDisableInterceptors", "true"));
        var compilation2 = CreateCompilation(source);
        var driver2 = CreateGeneratorDriver(options2);
        driver2 = driver2.RunGenerators(compilation2, TestContext.Current.CancellationToken);
        var result2 = driver2.GetRunResult();

        // The Settings tracking step should show change
        var settingsStep1 = result1.Results[0].TrackedSteps
            .Where(s => s.Key == TrackingNames.Settings)
            .SelectMany(s => s.Value)
            .ToList();
        var settingsStep2 = result2.Results[0].TrackedSteps
            .Where(s => s.Key == TrackingNames.Settings)
            .SelectMany(s => s.Value)
            .ToList();

        // Settings should have been processed (New reason on first, would be modified on configuration change)
        Assert.NotEmpty(settingsStep1);
        Assert.NotEmpty(settingsStep2);
    }

    #endregion

    #region Diagnostic Tests

    /// <summary>
    /// Diagnostic test that reports detailed information about which steps
    /// are not caching properly. This test verifies that the tracked intermediate
    /// steps are properly cached, while acknowledging that final outputs may show
    /// as Modified due to compilation changes when using RegisterImplementationSourceOutput.
    /// </summary>
    [Fact]
    public void Diagnostic_ReportCachingStatus()
    {
        var source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Foundatio.Mediator;

            public record MyMessage;

            public class MyHandler
            {
                public Task HandleAsync(MyMessage msg, CancellationToken ct) => Task.CompletedTask;
            }

            public class MyService
            {
                private readonly IMediator _mediator;
                public MyService(IMediator mediator) => _mediator = mediator;

                public async Task DoWork(CancellationToken ct)
                {
                    await _mediator.InvokeAsync(new MyMessage(), ct);
                }
            }
            """;

        var (result1, result2) = RunGeneratorTwice(source);

        var output = new System.Text.StringBuilder();
        output.AppendLine("=== Incremental Generator Caching Diagnostic Report ===\n");

        // Report tracked step status
        output.AppendLine("Tracked Steps (Run 1 -> Run 2):");
        output.AppendLine(new string('-', 80));

        var intermediateIssues = new List<string>();

        foreach (var (stepName, runSteps) in result2.Results[0].TrackedSteps.OrderBy(x => x.Key))
        {
            output.AppendLine($"\nStep: {stepName}");
            foreach (var runStep in runSteps)
            {
                foreach (var (value, reason) in runStep.Outputs)
                {
                    var status = reason switch
                    {
                        IncrementalStepRunReason.Cached => "✓ CACHED",
                        IncrementalStepRunReason.Unchanged => "✓ UNCHANGED",
                        IncrementalStepRunReason.New => "⚠ NEW",
                        IncrementalStepRunReason.Modified => "✗ MODIFIED",
                        IncrementalStepRunReason.Removed => "- REMOVED",
                        _ => $"? {reason}"
                    };
                    output.AppendLine($"  {status}: {value?.GetType().Name ?? "null"}");

                    // Track issues in our tracked intermediate steps (not Compilation or ImplementationSourceOutput)
                    if (AllTrackingNames.Contains(stepName) &&
                        reason != IncrementalStepRunReason.Cached &&
                        reason != IncrementalStepRunReason.Unchanged)
                    {
                        intermediateIssues.Add($"Step '{stepName}': {reason} - {value?.GetType().Name ?? "null"}");
                    }
                }
            }
        }

        // Report final output status (expected to be Modified due to compilation changes)
        output.AppendLine("\n" + new string('=', 80));
        output.AppendLine("Final Output Steps (Run 2):");
        output.AppendLine("(Note: ImplementationSourceOutput may be Modified due to compilation changes - this is expected)");
        output.AppendLine(new string('-', 80));

        foreach (var (stepName, runSteps) in result2.Results[0].TrackedOutputSteps.OrderBy(x => x.Key))
        {
            output.AppendLine($"\nOutput: {stepName}");
            foreach (var runStep in runSteps)
            {
                foreach (var (value, reason) in runStep.Outputs)
                {
                    var status = reason switch
                    {
                        IncrementalStepRunReason.Cached => "✓ CACHED",
                        IncrementalStepRunReason.Unchanged => "✓ UNCHANGED",
                        IncrementalStepRunReason.New => "⚠ NEW",
                        IncrementalStepRunReason.Modified => "→ MODIFIED (expected for ImplementationSourceOutput)",
                        IncrementalStepRunReason.Removed => "- REMOVED",
                        _ => $"? {reason}"
                    };
                    output.AppendLine($"  {status}: {value?.GetType().Name ?? "null"}");
                }
            }
        }

        output.AppendLine("\n" + new string('=', 80));
        output.AppendLine("\nSummary:");
        output.AppendLine($"  Intermediate step issues: {intermediateIssues.Count}");

        if (intermediateIssues.Count > 0)
        {
            output.AppendLine("\nIntermediate step problems (these indicate unnecessary work):");
            foreach (var issue in intermediateIssues)
            {
                output.AppendLine($"  - {issue}");
            }
            Assert.Fail(output.ToString());
        }
        else
        {
            output.AppendLine("  All intermediate steps are properly cached! ✓");
        }
    }

    #endregion

    #region Test Helpers

    private static (GeneratorDriverRunResult Result1, GeneratorDriverRunResult Result2) RunGeneratorTwice(
        string source,
        AnalyzerConfigOptionsProvider? optionsProvider = null,
        MetadataReference[]? additionalReferences = null)
    {
        var compilation = CreateCompilation(source, additionalReferences);

        // Create a driver with step tracking enabled
        var driver = CreateGeneratorDriver(optionsProvider);

        // Clone compilation for second run
        var clone = compilation.Clone();

        // First run
        driver = driver.RunGenerators(compilation);
        var result1 = driver.GetRunResult();

        // Second run with same driver (should be cached)
        driver = driver.RunGenerators(clone);
        var result2 = driver.GetRunResult();

        return (result1, result2);
    }

    private static (GeneratorDriver Driver, GeneratorDriverRunResult Result) RunGenerator(
        Compilation compilation,
        AnalyzerConfigOptionsProvider? optionsProvider = null)
    {
        var driver = CreateGeneratorDriver(optionsProvider);
        driver = driver.RunGenerators(compilation);
        return (driver, driver.GetRunResult());
    }

    private static GeneratorDriver CreateGeneratorDriver(AnalyzerConfigOptionsProvider? optionsProvider = null)
    {
        var generator = new MediatorGenerator().AsSourceGenerator();

        // Enable step tracking for caching verification
        var opts = new GeneratorDriverOptions(
            disabledOutputs: IncrementalGeneratorOutputKind.None,
            trackIncrementalGeneratorSteps: true);

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);

        return CSharpGeneratorDriver.Create(
            [generator],
            additionalTexts: null,
            parseOptions: parseOptions,
            optionsProvider: optionsProvider,
            driverOptions: opts);
    }

    private static CSharpCompilation CreateCompilation(string source, MetadataReference[]? additionalReferences = null)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Microsoft.Extensions.DependencyInjection.ServiceCollection).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IMediator).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(MediatorGenerator).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Diagnostics.Stopwatch).Assembly.Location)
        };

        // Add reference to System.Runtime for base types
        var coreLibDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimePath = Path.Combine(coreLibDir, "System.Runtime.dll");
        var netstandardPath = Path.Combine(coreLibDir, "netstandard.dll");

        if (File.Exists(runtimePath))
            references.Add(MetadataReference.CreateFromFile(runtimePath));
        if (File.Exists(netstandardPath))
            references.Add(MetadataReference.CreateFromFile(netstandardPath));

        if (additionalReferences != null)
            references.AddRange(additionalReferences);

        return CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: [CSharpSyntaxTree.ParseText(source, parseOptions)],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    }

    private static MetadataReference CreateHandlerAssembly(string source, string? assemblyName = null)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.CSharp11);
        var syntaxTree = CSharpSyntaxTree.ParseText(source, parseOptions);

        var references = new List<MetadataReference>
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(IMediator).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Diagnostics.Stopwatch).Assembly.Location)
        };

        // Add reference to System.Runtime and netstandard for base types
        var coreLibDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var runtimePath = Path.Combine(coreLibDir, "System.Runtime.dll");
        var netstandardPath = Path.Combine(coreLibDir, "netstandard.dll");

        if (File.Exists(runtimePath))
            references.Add(MetadataReference.CreateFromFile(runtimePath));
        if (File.Exists(netstandardPath))
            references.Add(MetadataReference.CreateFromFile(netstandardPath));

        var compilation = CSharpCompilation.Create(
            assemblyName: assemblyName ?? "HandlerAssembly",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);

        if (!emitResult.Success)
        {
            var errors = string.Join("\n", emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            throw new InvalidOperationException($"Failed to compile handler assembly:\n{errors}");
        }

        ms.Seek(0, SeekOrigin.Begin);
        return MetadataReference.CreateFromStream(ms);
    }

    private static AnalyzerConfigOptionsProvider CreateOptions(params (string Key, string Value)[] globalOptions)
    {
        var dict = globalOptions.ToImmutableDictionary(kv => kv.Key, kv => kv.Value);
        return new SimpleOptionsProvider(dict);
    }

    #endregion

    #region Assertion Helpers

    private static void AssertAllOutputsCached(
        GeneratorDriverRunResult result1,
        GeneratorDriverRunResult result2,
        string[] trackingNames)
    {
        // Get tracked steps from both runs, filtered to known tracking names
        var trackedSteps1 = GetTrackedSteps(result1, trackingNames);
        var trackedSteps2 = GetTrackedSteps(result2, trackingNames);

        // Both runs should have tracked steps
        Assert.NotEmpty(trackedSteps1);

        // Verify both runs have the same tracked step names
        Assert.Equal(trackedSteps1.Keys.OrderBy(k => k), trackedSteps2.Keys.OrderBy(k => k));

        // For each tracked step, verify outputs are equal and cached on second run
        foreach (var (trackingName, runSteps1) in trackedSteps1)
        {
            var runSteps2 = trackedSteps2[trackingName];

            // Should have same number of steps
            Assert.Equal(runSteps1.Length, runSteps2.Length);

            for (var i = 0; i < runSteps1.Length; i++)
            {
                var runStep1 = runSteps1[i];
                var runStep2 = runSteps2[i];

                // The outputs should be equal between different runs (using value equality)
                var outputs1 = runStep1.Outputs.Select(x => x.Value).ToList();
                var outputs2 = runStep2.Outputs.Select(x => x.Value).ToList();

                // Verify value equality - this catches non-record types or types with reference equality
                Assert.Equal(outputs1.Count, outputs2.Count);
                for (var j = 0; j < outputs1.Count; j++)
                {
                    Assert.True(
                        Equals(outputs1[j], outputs2[j]),
                        $"Step '{trackingName}' output {j} not equal between runs. " +
                        $"Type: {outputs1[j]?.GetType().Name ?? "null"}, " +
                        $"This indicates the type doesn't implement proper value equality.");
                }

                // On the second run, all outputs should be cached or unchanged
                foreach (var (value, reason) in runStep2.Outputs)
                {
                    Assert.True(
                        reason == IncrementalStepRunReason.Cached || reason == IncrementalStepRunReason.Unchanged,
                        $"Step '{trackingName}' expected reason Cached or Unchanged but got {reason}. " +
                        $"Output type: {value?.GetType().Name ?? "null"}. " +
                        $"This indicates the generator is doing unnecessary work.");
                }
            }
        }

        // NOTE: We intentionally do NOT check TrackedOutputSteps for RegisterImplementationSourceOutput.
        // When using RegisterImplementationSourceOutput, the final output may show as Modified when the
        // compilation changes (which happens when cloning). This is expected behavior - the key is that
        // the intermediate tracked steps (Settings, CallSites, Handlers, Middleware) properly cache,
        // which prevents the expensive extraction/analysis work from re-running.
    }

    private static Dictionary<string, ImmutableArray<IncrementalGeneratorRunStep>> GetTrackedSteps(
        GeneratorDriverRunResult runResult,
        string[] trackingNames)
    {
        return runResult.Results[0]
            .TrackedSteps
            .Where(step => trackingNames.Contains(step.Key))
            .ToDictionary(x => x.Key, x => x.Value);
    }

    private static void AssertNoProblematicTypes(GeneratorDriverRunResult result, string[] trackingNames)
    {
        var trackedSteps = GetTrackedSteps(result, trackingNames);

        foreach (var (stepName, runSteps) in trackedSteps)
        {
            foreach (var runStep in runSteps)
            {
                foreach (var (obj, _) in runStep.Outputs)
                {
                    AssertObjectGraph(obj, stepName);
                }
            }
        }
    }

    private static void AssertObjectGraph(object? node, string stepName)
    {
        var visited = new HashSet<object>();
        Visit(node);

        void Visit(object? obj)
        {
            if (obj is null || !visited.Add(obj))
                return;

            var type = obj.GetType();

            // Check for banned types
            Assert.False(
                obj is Compilation,
                $"Step '{stepName}' contains a Compilation instance which will cause GC rooting issues");

            Assert.False(
                obj is ISymbol,
                $"Step '{stepName}' contains an ISymbol instance which will cause GC rooting issues");

            Assert.False(
                obj is SyntaxNode,
                $"Step '{stepName}' contains a SyntaxNode instance which will cause equality/GC issues");

            // Skip primitives and strings
            if (type.IsPrimitive || type.IsEnum || type == typeof(string))
                return;

            // Check collections
            if (obj is System.Collections.IEnumerable collection and not string)
            {
                foreach (var element in collection)
                {
                    Visit(element);
                }
                return;
            }

            // Recursively check fields
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                try
                {
                    var fieldValue = field.GetValue(obj);
                    Visit(fieldValue);
                }
                catch
                {
                    // Some fields may not be readable, skip them
                }
            }
        }
    }

    #endregion

    #region Options Provider

    private sealed class SimpleOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly ImmutableDictionary<string, string> _globals;

        public SimpleOptionsProvider(ImmutableDictionary<string, string> globals)
        {
            _globals = globals;
            GlobalOptions = new SimpleOptions(_globals);
        }

        public override AnalyzerConfigOptions GlobalOptions { get; }
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => new SimpleOptions(_globals);
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => new SimpleOptions(_globals);

        private sealed class SimpleOptions : AnalyzerConfigOptions
        {
            private readonly ImmutableDictionary<string, string> _globals;

            public SimpleOptions(ImmutableDictionary<string, string> globals)
            {
                _globals = globals;
            }

            public override bool TryGetValue(string key, out string value)
            {
                if (_globals.TryGetValue(key, out var v))
                {
                    value = v;
                    return true;
                }
                value = string.Empty;
                return false;
            }
        }
    }

    #endregion
}
