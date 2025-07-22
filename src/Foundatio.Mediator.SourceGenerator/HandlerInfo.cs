namespace Foundatio.Mediator;

public readonly record struct HandlerInfo
{
    public readonly string HandlerTypeName;
    public readonly string MessageTypeName;
    public readonly string MethodName;
    public readonly string ReturnTypeName;
    public readonly string OriginalReturnTypeName;
    public readonly bool IsAsync;
    public readonly bool IsStatic;
    public readonly EquatableArray<ParameterInfo> Parameters;
    public readonly EquatableArray<string> MessageTypeHierarchy;

    public HandlerInfo(
        string handlerTypeName,
        string messageTypeName,
        string methodName,
        string returnTypeName,
        string originalReturnTypeName,
        bool isAsync,
        bool isStatic,
        List<ParameterInfo> parameters,
        List<string> messageTypeHierarchy)
    {
        HandlerTypeName = handlerTypeName;
        MessageTypeName = messageTypeName;
        MethodName = methodName;
        ReturnTypeName = returnTypeName;
        OriginalReturnTypeName = originalReturnTypeName;
        IsAsync = isAsync;
        IsStatic = isStatic;
        Parameters = new(parameters.ToArray());
        MessageTypeHierarchy = new(messageTypeHierarchy.ToArray());
    }
}

public readonly record struct ParameterInfo : IEquatable<ParameterInfo>
{
    public readonly string Name;
    public readonly string TypeName;
    public readonly bool IsMessage;
    public readonly bool IsCancellationToken;

    public ParameterInfo(string name, string typeName, bool isMessage, bool isCancellationToken)
    {
        Name = name;
        TypeName = typeName;
        IsMessage = isMessage;
        IsCancellationToken = isCancellationToken;
    }

    public bool Equals(ParameterInfo other)
    {
        return Name == other.Name &&
               TypeName == other.TypeName &&
               IsMessage == other.IsMessage &&
               IsCancellationToken == other.IsCancellationToken;
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            hash = hash * 31 + (Name?.GetHashCode() ?? 0);
            hash = hash * 31 + (TypeName?.GetHashCode() ?? 0);
            hash = hash * 31 + IsMessage.GetHashCode();
            hash = hash * 31 + IsCancellationToken.GetHashCode();
            return hash;
        }
    }
}
