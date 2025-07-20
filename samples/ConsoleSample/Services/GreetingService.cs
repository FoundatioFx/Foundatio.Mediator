namespace ConsoleSample.Services;

public interface IGreetingService
{
    string CreateGreeting(string name);
}

public class GreetingService : IGreetingService
{
    public string CreateGreeting(string name)
    {
        return $"Hello {name}, welcome to our amazing application!";
    }
}
