namespace Foundatio.Mediator;

/// <summary>
/// Marker interface for classes that should be treated as mediator handlers.
/// Apply this interface to a class to opt it into handler discovery without using naming conventions
/// (e.g., <c>*Handler</c>, <c>*Consumer</c>) or the <c>[Handler]</c> attribute.
/// </summary>
public interface IHandler { }
