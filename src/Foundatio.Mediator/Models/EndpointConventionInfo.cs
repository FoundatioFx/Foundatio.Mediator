using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator.Models;

/// <summary>
/// The scope at which an endpoint convention attribute was applied.
/// </summary>
internal enum ConventionScope
{
    /// <summary>Applied at the assembly level (global default).</summary>
    Assembly,
    /// <summary>Applied at the handler class level.</summary>
    Class,
    /// <summary>Applied at the handler method level (most specific).</summary>
    Method
}

/// <summary>
/// Contains metadata for an endpoint convention attribute that implements
/// <c>IEndpointConvention&lt;TBuilder&gt;</c>. Used by the generator to emit
/// code that instantiates the attribute and calls <c>Configure(builder)</c>.
/// </summary>
internal readonly record struct EndpointConventionInfo
{
    /// <summary>
    /// The fully qualified type name of the convention attribute.
    /// </summary>
    public string AttributeTypeName { get; init; }

    /// <summary>
    /// The fully qualified type name of TBuilder in IEndpointConvention&lt;TBuilder&gt;.
    /// Used to determine whether this convention targets endpoint builders or group builders.
    /// </summary>
    public string BuilderTypeName { get; init; }

    /// <summary>
    /// The scope at which this convention attribute was applied (Assembly, Class, or Method).
    /// Used for most-derived-wins deduplication: Method overrides Class overrides Assembly.
    /// </summary>
    public ConventionScope Scope { get; init; }

    /// <summary>
    /// Constructor arguments for reconstructing the attribute instance.
    /// </summary>
    public EquatableArray<AttributeValueInfo> ConstructorArguments { get; init; }

    /// <summary>
    /// Named property assignments for reconstructing the attribute instance.
    /// </summary>
    public EquatableArray<NamedAttributeArgumentInfo> NamedArguments { get; init; }
}
