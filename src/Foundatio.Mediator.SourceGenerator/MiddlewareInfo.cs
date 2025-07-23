namespace Foundatio.Mediator;

public class MiddlewareInfo(
    string middlewareTypeName,
    string messageTypeName,
    bool isObjectType,
    bool isInterfaceType,
    List<string> interfaceTypes,
    MiddlewareMethodInfo? beforeMethod,
    MiddlewareMethodInfo? afterMethod,
    MiddlewareMethodInfo? finallyMethod,
    int? order = null)
{
    public string MiddlewareTypeName { get; } = middlewareTypeName;
    public string MessageTypeName { get; } = messageTypeName;
    public bool IsObjectType { get; } = isObjectType;
    public bool IsInterfaceType { get; } = isInterfaceType;
    public List<string> InterfaceTypes { get; } = interfaceTypes;
    public MiddlewareMethodInfo? BeforeMethod { get; } = beforeMethod;
    public MiddlewareMethodInfo? AfterMethod { get; } = afterMethod;
    public MiddlewareMethodInfo? FinallyMethod { get; } = finallyMethod;
    public int? Order { get; } = order;
    public bool IsAsync { get; } = (beforeMethod?.IsAsync == true) || (afterMethod?.IsAsync == true) || (finallyMethod?.IsAsync == true);
}

public class MiddlewareMethodInfo(
    string methodName,
    bool isAsync,
    string returnTypeName,
    List<ParameterInfo> parameters)
{
    public string MethodName { get; } = methodName;
    public bool IsAsync { get; } = isAsync;
    public string ReturnTypeName { get; } = returnTypeName;
    public List<ParameterInfo> Parameters { get; } = parameters;
}
