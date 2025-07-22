using ConsoleSample.Messages;
using Foundatio.Mediator;

public class CreateUserCommandHandler
{
    public async Task<Result<User>> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
    {
        // Simulate some business logic
        await Task.Delay(100, cancellationToken);

        if (command.Email == "existing@example.com")
            return Result.Conflict("A user with this email already exists");

        // Create the user
        var user = new User
        {
            Id = Random.Shared.Next(1000, 9999),
            Name = command.Name,
            Email = command.Email,
            Age = command.Age,
            PhoneNumber = command.PhoneNumber,
            CreatedAt = DateTime.UtcNow
        };

        Console.WriteLine($"✅ [CreateUserCommandHandler] Successfully created user with ID: {user.Id}");

        // User implicitly converted to Result<User>
        return user;
    }
}
