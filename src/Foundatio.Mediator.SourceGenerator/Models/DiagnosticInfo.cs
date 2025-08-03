using Foundatio.Mediator.Models;
using Microsoft.CodeAnalysis;

internal readonly record struct DiagnosticInfo
{
    public string Identifier { get; init; }
    public string Title { get; init; }
    public string Message { get; init; }
    public DiagnosticSeverity Severity { get; init; }
    public LocationInfo? Location { get; init; }

    public Diagnostic ToDiagnostic()
    {
        return Diagnostic.Create(new DiagnosticDescriptor(Identifier, Title, Message, "Usage", Severity, true), Location?.ToLocation());
    }
}
