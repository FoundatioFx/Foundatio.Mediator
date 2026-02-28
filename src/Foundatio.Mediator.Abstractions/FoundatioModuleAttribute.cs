namespace Foundatio.Mediator;

/// <summary>
/// Assembly-level attribute emitted by the source generator to mark assemblies
/// that contain mediator handler registrations. Used at runtime by <c>AddMediator</c> to discover modules.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class FoundatioModuleAttribute : Attribute { }
