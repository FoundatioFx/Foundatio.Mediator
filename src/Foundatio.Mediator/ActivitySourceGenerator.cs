using Foundatio.Mediator.Utility;
using Microsoft.CodeAnalysis;

namespace Foundatio.Mediator;

internal static class ActivitySourceGenerator
{
    public static void Execute(SourceProductionContext context, bool openTelemetryEnabled)
    {
        if (!openTelemetryEnabled)
            return;

        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader();
        source.AppendLine("using System.Diagnostics;");
        source.AppendLine();
        source.AppendLine("namespace Foundatio.Mediator;");
        source.AppendLine();
        source.AddGeneratedCodeAttribute();
        source.AppendLine("internal static class MediatorActivitySource");
        source.AppendLine("{");
        source.AppendLine("    internal static readonly ActivitySource Instance = new(\"Foundatio.Mediator\");");
        source.AppendLine("}");

        context.AddSource("MediatorActivitySource.g.cs", source.ToString());
    }
}