namespace Foundatio.Mediator.Models;

internal readonly record struct LocationInfo
{
    public string FilePath { get; init; }
    public TextSpan TextSpan { get; init; }
    public LinePositionSpan LineSpan { get; init; }
    public int Version { get; init; }
    public string Data { get; init; }
    public string DisplayLocation { get; init; }

    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);
    public static LocationInfo? CreateFrom(SyntaxNode node, InterceptableLocation? interceptableLocation = null) => CreateFrom(node.GetLocation(), interceptableLocation);

    public static LocationInfo? CreateFrom(Location location, InterceptableLocation? interceptableLocation = null)
    {
        if (location.SourceTree is null)
            return null;

        return new LocationInfo
        {
            FilePath = location.SourceTree.FilePath,
            TextSpan = location.SourceSpan,
            LineSpan = location.GetLineSpan().Span,
            Version = interceptableLocation?.Version ?? 0,
            Data = interceptableLocation?.Data ?? string.Empty,
            DisplayLocation = interceptableLocation?.GetDisplayLocation() ?? String.Empty
        };
    }
}
