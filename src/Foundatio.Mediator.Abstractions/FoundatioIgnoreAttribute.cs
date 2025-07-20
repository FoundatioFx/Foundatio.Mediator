namespace Foundatio.Mediator;

/// <summary>
/// Indicates that a class or method should be ignored by the Foundatio Mediator handler generator.
/// When applied to a class, all handler methods in that class will be ignored.
/// When applied to a method, only that specific handler method will be ignored.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class FoundatioIgnoreAttribute : Attribute
{
}
