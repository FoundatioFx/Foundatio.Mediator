namespace Foundatio.Mediator.Models;

/// <summary>
/// Captures compilation-level information needed by downstream generators,
/// avoiding the need to pipe the full <c>Compilation</c> through the incremental pipeline.
/// </summary>
internal readonly record struct CompilationInfo(
    string AssemblyName,
    bool SupportsMinimalApis,
    bool HasAsParametersAttribute,
    bool HasFromBodyAttribute,
    bool HasWithOpenApi);
