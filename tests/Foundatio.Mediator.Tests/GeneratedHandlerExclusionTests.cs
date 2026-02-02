namespace Foundatio.Mediator.Tests;

public class GeneratedHandlerExclusionTests(ITestOutputHelper output) : GeneratorTestBase(output)
{
    [Fact]
    public void DoesNotGenerateHandlerForGeneratedHandlerClasses()
    {
        var source = """
            using Foundatio.Mediator;

            namespace Foundatio.Mediator
            {
                // Simulate a generated handler class that someone might have included in their project
                internal static class SomeHandler_SomeMessage_Handler
                {
                    public static string Handle(IMediator mediator, string message, CancellationToken cancellationToken)
                    {
                        return "handled";
                    }
                }
            }

            namespace MyApp
            {
                public record TestMessage(string Value);

                public class Test_Message_Handler
                {
                    public string Handle(TestMessage message) => $"Handled: {message.Value}";
                }
            }
            """;

        var (_, _, trees) = RunGenerator(source, [new MediatorGenerator()]);

        // Should only generate a handler for TestMessageHandler, not for the simulated generated class
        var handlerFiles = trees.Where(t => t.HintName.EndsWith("_Handler.g.cs")).ToList();

        // Should only have one handler generated (for TestMessageHandler)
        Assert.Single(handlerFiles);

        var handlerFile = handlerFiles.Single();
        Assert.Contains("Test_Message_Handler_TestMessage_Handler", handlerFile.HintName);

        // Should not generate a handler for the simulated generated class
        Assert.DoesNotContain("SomeHandler_SomeMessage_Handler", handlerFile.Source);
    }
}
