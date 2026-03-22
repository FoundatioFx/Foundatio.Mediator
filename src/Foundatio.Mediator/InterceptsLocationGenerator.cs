using Foundatio.Mediator.Models;
using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator;

internal static class InterceptsLocationGenerator
{
    public static void Execute(SourceProductionContext context, GeneratorConfiguration configuration)
    {
        if (!configuration.InterceptorsEnabled)
            return;

        const string hintName = "_InterceptsLocationAttribute.g.cs";
        var source = new IndentedStringBuilder();

        source.AddGeneratedFileHeader(configuration.GenerationCounterEnabled, hintName);
        source.AppendLine("using System;");
        source.AppendLine();
        source.AppendLine("namespace System.Runtime.CompilerServices;");
        source.AppendLine();
        source.AppendLines("""
            /// <summary>
            /// Indicates that a method is an interceptor and provides the location of the intercepted call.
            /// </summary>
            """);
        source.AddGeneratedCodeAttribute();
        source.AppendLines("""
            [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]
            internal sealed class InterceptsLocationAttribute : global::System.Attribute
            {
                /// <summary>
                /// Initializes a new instance of the <see cref="InterceptsLocationAttribute"/> class.
                /// </summary>
                /// <param name="version">The version of the location encoding.</param>
                /// <param name="data">The encoded location data.</param>
                public InterceptsLocationAttribute(int version, string data)
                {
                    Version = version;
                    Data = data;
                }

                /// <summary>
                /// Gets the version of the location encoding.
                /// </summary>
                public int Version { get; }

                /// <summary>
                /// Gets the encoded location data.
                /// </summary>
                public string Data { get; }
            }
            """);

        context.AddSource(hintName, source.ToString());
    }
}
