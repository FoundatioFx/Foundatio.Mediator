using Foundatio.Mediator;

namespace Common.Module.Messages;

/// <summary>
/// Authenticates a user with username and password, returning user info on success.
/// </summary>
public record Login(string Username, string Password);

/// <summary>
/// Signs out the current user.
/// </summary>
public record Logout;

/// <summary>
/// Returns the current authenticated user's info, or Unauthorized if not logged in.
/// </summary>
public record GetCurrentUser;

public record UserInfo(string DisplayName, string Username, string Role);
