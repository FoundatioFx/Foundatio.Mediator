using Foundatio.Mediator.Utility;
using Microsoft.CodeAnalysis;

namespace Foundatio.Mediator;

internal static class InterceptsLocationGenerator
{
    public static void Execute(SourceProductionContext context, bool interceptorsEnabled)
    {
        if (!interceptorsEnabled)
            return;

        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader();
        source.AppendLine("using System;");
        source.AppendLine();
        source.AppendLine("namespace System.Runtime.CompilerServices;");
        source.AppendLine();
        source.AppendLine("/// <summary>");
        source.AppendLine("/// Indicates that a method is an interceptor and provides the location of the intercepted call.");
        source.AppendLine("/// </summary>");
        source.AppendLine("[global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]");
        source.AppendLine("internal sealed class InterceptsLocationAttribute : global::System.Attribute");
        source.AppendLine("{");
        source.AppendLine("    /// <summary>");
        source.AppendLine("    /// Initializes a new instance of the <see cref=\"InterceptsLocationAttribute\"/> class.");
        source.AppendLine("    /// </summary>");
        source.AppendLine("    /// <param name=\"version\">The version of the location encoding.</param>");
        source.AppendLine("    /// <param name=\"data\">The encoded location data.</param>");
        source.AppendLine("    public InterceptsLocationAttribute(int version, string data)");
        source.AppendLine("    {");
        source.AppendLine("        Version = version;");
        source.AppendLine("        Data = data;");
        source.AppendLine("    }");
        source.AppendLine();
        source.AppendLine("    /// <summary>");
        source.AppendLine("    /// Gets the version of the location encoding.");
        source.AppendLine("    /// </summary>");
        source.AppendLine("    public int Version { get; }");
        source.AppendLine();
        source.AppendLine("    /// <summary>");
        source.AppendLine("    /// Gets the encoded location data.");
        source.AppendLine("    /// </summary>");
        source.AppendLine("    public string Data { get; }");
        source.AppendLine("}");

        context.AddSource("InterceptsLocationAttribute.g.cs", source.ToString());
    }
}
