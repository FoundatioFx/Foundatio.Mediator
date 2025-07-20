namespace Foundatio.Mediator;

public readonly record struct HandlerToGenerate
{
    public readonly string HandlerTypeName;
    public readonly string MessageTypeName;
    public readonly string MethodName;
    public readonly string ReturnTypeName;
    public readonly bool IsAsync;
    public readonly EquatableArray<ParameterInfo> Parameters;

    public HandlerToGenerate(
        string handlerTypeName,
        string messageTypeName,
        string methodName,
        string returnTypeName,
        bool isAsync,
        List<ParameterInfo> parameters)
    {
        HandlerTypeName = handlerTypeName;
        MessageTypeName = messageTypeName;
        MethodName = methodName;
        ReturnTypeName = returnTypeName;
        IsAsync = isAsync;
        Parameters = new(parameters.ToArray());
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