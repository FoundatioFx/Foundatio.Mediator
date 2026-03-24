using Foundatio.Mediator.Utility;

namespace Foundatio.Mediator.Models;

internal readonly record struct HandlerAttributeMetadataInfo
{
    public string AttributeTypeName { get; init; }
    public bool IsMethodLevel { get; init; }
    public EquatableArray<AttributeValueInfo> ConstructorArguments { get; init; }
    public EquatableArray<NamedAttributeArgumentInfo> NamedArguments { get; init; }
}

internal readonly record struct AttributeValueInfo
{
    public string? Value { get; init; }
}

internal readonly record struct NamedAttributeArgumentInfo
{
    public string Name { get; init; }
    public string? Value { get; init; }
}
