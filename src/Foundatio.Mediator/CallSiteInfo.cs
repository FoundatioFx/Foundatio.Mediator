using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Foundatio.Mediator;

public readonly record struct CallSiteInfo
{
    public readonly string MethodName;
    public readonly string MessageTypeName;
    public readonly string ExpectedResponseTypeName;
    public readonly bool IsAsync;
    public readonly bool IsPublish;
    public readonly Location Location;
    public readonly InterceptableLocation InterceptableLocation;

    public CallSiteInfo(
        string methodName,
        string messageTypeName,
        string expectedResponseTypeName,
        bool isAsync,
        bool isPublish,
        Location location,
        InterceptableLocation invocationSyntax)
    {
        MethodName = methodName;
        MessageTypeName = messageTypeName;
        ExpectedResponseTypeName = expectedResponseTypeName;
        IsAsync = isAsync;
        IsPublish = isPublish;
        Location = location;
        InterceptableLocation = invocationSyntax;
    }
}
