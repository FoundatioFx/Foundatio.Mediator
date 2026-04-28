// Licensed to the .NET Foundation under one or more agreements.
// Polyfill for netstandard2.0 — attribute is recognized by the compiler/IDE even when defined internally.
#if NETSTANDARD2_0

namespace System.Diagnostics.CodeAnalysis;

[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
internal sealed class StringSyntaxAttribute : Attribute
{
    public StringSyntaxAttribute(string syntax) => Syntax = syntax;
    public string Syntax { get; }

    public const string Route = nameof(Route);
}

#endif
