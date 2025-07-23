using System.ComponentModel.DataAnnotations;
using Foundatio.Mediator;

namespace ConsoleSample.Messages;

public class CreateUserCommand : ICommand
{
    [Required(ErrorMessage = "Name is required")]
    [StringLength(50, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 50 characters")]
    public string Name { get; set; } = String.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email format")]
    public string Email { get; set; } = String.Empty;

    [Range(18, 120, ErrorMessage = "Age must be between 18 and 120")]
    public int Age { get; set; }

    [Phone(ErrorMessage = "Invalid phone number format")]
    public string? PhoneNumber { get; set; }
}

public record User
{
    public int Id { get; set; }
    public string Name { get; set; } = String.Empty;
    public string Email { get; set; } = String.Empty;
    public int Age { get; set; }
    public string? PhoneNumber { get; set; }
    public DateTime CreatedAt { get; set; }
}

public record UserCreatedEvent(User User) : INotification
{
    public string Id => User.Id.ToString();
}
