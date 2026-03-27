namespace Common.Module.Services;

/// <summary>
/// Simple demo user store for the sample application.
/// </summary>
public interface IDemoUserService
{
    bool TryGetUser(string username, out DemoUser user);
}

public record DemoUser(string Username, string Password, string DisplayName, string Role);

public class DemoUserService : IDemoUserService
{
    private readonly Dictionary<string, DemoUser> _users = new(StringComparer.OrdinalIgnoreCase)
    {
        ["admin"] = new("admin", "admin", "Alice Admin", "Admin"),
        ["user"]  = new("user",  "user",  "Bob User",   "User"),
    };

    public bool TryGetUser(string username, out DemoUser user)
    {
        if (_users.TryGetValue(username, out var found))
        {
            user = found;
            return true;
        }

        user = default!;
        return false;
    }
}
