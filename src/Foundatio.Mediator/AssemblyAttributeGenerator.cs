using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

internal static class AssemblyAttributeGenerator
{
    /// <summary>
    /// Generates the [assembly: FoundatioModule] attribute to mark assemblies that contain handlers or middleware.
    /// This attribute is used by MetadataMiddlewareScanner to discover middleware in referenced assemblies.
    /// </summary>
    public static void Execute(SourceProductionContext context, Compilation compilation)
    {
        var assemblyName = compilation.AssemblyName?.ToIdentifier() ?? Guid.NewGuid().ToString("N").Substring(0, 10);
        var className = $"{assemblyName}_FoundatioModuleAttribute";

        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader();
        source.AppendLine();
        source.AppendLine("[assembly: Foundatio.Mediator.FoundatioModule]");

        context.AddSource($"{className}.g.cs", source.ToString());
    }
}
