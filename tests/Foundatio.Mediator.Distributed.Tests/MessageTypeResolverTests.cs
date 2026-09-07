namespace Foundatio.Mediator.Distributed.Tests;

public class MessageTypeResolverTests
{
    [Fact]
    public void LoadedGenericType_ResolvesOnlyWhenAssignable()
    {
        var resolver = new MessageTypeResolver();
        var type = typeof(List<string>);
        Assert.Equal(type, resolver.TryResolve(type.AssemblyQualifiedName!, typeof(IEnumerable<string>)));
        Assert.Null(resolver.TryResolve(type.AssemblyQualifiedName!, typeof(IEnumerable<int>)));
    }

    [Theory]
    [InlineData("Invalid[[")]
    [InlineData("System.String, Missing.Assembly")]
    [InlineData("")]
    public void UnknownOrMalformedName_ReturnsNull(string name)
        => Assert.Null(new MessageTypeResolver().TryResolve(name, typeof(object)));
}
