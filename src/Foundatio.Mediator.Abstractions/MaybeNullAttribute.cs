// Licensed to the .NET Foundation under one or more agreements.
// Polyfill for netstandard2.0 — attribute is recognized by the compiler even when defined internally.
#if NETSTANDARD2_0

namespace System.Diagnostics.CodeAnalysis;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.ReturnValue, Inherited = false)]
internal sealed class MaybeNullAttribute : Attribute;

#endif
