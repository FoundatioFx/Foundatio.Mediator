using System.Text;

namespace Foundatio.Mediator;

public static class MessageTypeKey
{
    public static string Get(Type type)
    {
        if (type == null) throw new ArgumentNullException(nameof(type));

        if (!type.IsGenericType || type.ContainsGenericParameters)
            return type.FullName!;

        return Build(type);
    }

    private static string Build(Type type)
    {
        if (!type.IsGenericType)
            return type.FullName!;

        var sb = new StringBuilder();
        sb.Append(type.Namespace);
        sb.Append('.');
        var backtickIndex = type.Name.IndexOf('`');
        var simpleName = backtickIndex > 0 ? type.Name.Substring(0, backtickIndex) : type.Name;
        sb.Append(simpleName);
        sb.Append('<');
        var args = type.GetGenericArguments();
        for (int i = 0; i < args.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Get(args[i]));
        }
        sb.Append('>');
        return sb.ToString();
    }
}
