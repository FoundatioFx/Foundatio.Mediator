using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Foundatio.Mediator.Utility;

internal readonly record struct LocationInfo
{
    public string FilePath { get; init; }
    public TextSpan TextSpan { get; init; }
    public LinePositionSpan LineSpan { get; init; }
    public string Version { get; init; }
    public string Data { get; init; }
    public string DisplayLocation { get; init; }

    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);
    public static LocationInfo? CreateFrom(SyntaxNode node) => CreateFrom(node.GetLocation());

    public static LocationInfo? CreateFrom(Location location)
    {
        if (location.SourceTree is null)
            return null;

        return new LocationInfo { FilePath = location.SourceTree.FilePath, TextSpan = location.SourceSpan, LineSpan = location.GetLineSpan().Span };
    }
}
