namespace Foundatio.Mediator;

internal class CallSiteKey
{
    public string MethodName { get; }
    public string MessageTypeName { get; }
    public string ExpectedResponseTypeName { get; }

    public CallSiteKey(string methodName, string messageTypeName, string expectedResponseTypeName)
    {
        MethodName = methodName;
        MessageTypeName = messageTypeName;
        ExpectedResponseTypeName = expectedResponseTypeName;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not CallSiteKey other)
            return false;

        return MethodName == other.MethodName &&
               MessageTypeName == other.MessageTypeName &&
               ExpectedResponseTypeName == other.ExpectedResponseTypeName;
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 23 + (MethodName?.GetHashCode() ?? 0);
            hash = hash * 23 + (MessageTypeName?.GetHashCode() ?? 0);
            hash = hash * 23 + (ExpectedResponseTypeName?.GetHashCode() ?? 0);
            return hash;
        }
    }
}
