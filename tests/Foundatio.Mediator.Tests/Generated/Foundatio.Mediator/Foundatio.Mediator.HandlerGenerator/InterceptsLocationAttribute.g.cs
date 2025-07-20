#nullable enable
using System;

namespace System.Runtime.CompilerServices
{
    /// <summary>
    /// Indicates that a method is an interceptor and provides the location of the intercepted call.
    /// </summary>
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
}
