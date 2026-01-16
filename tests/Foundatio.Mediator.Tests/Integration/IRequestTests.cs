using Foundatio.Xunit;
using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests.Integration;

public class IRequestTests(ITestOutputHelper output) : TestWithLoggingBase(output)
{
    // Test messages using IRequest<T>
    public record GetUser(int Id) : IRequest<User>;
    public record CreateUser(string Name) : ICommand<User>;
    public record FindUsers(string Query) : IQuery<List<User>>;

    // Test messages for cascading
    public record CreateUserWithEvent(string Name) : IRequest<User>;
    public record UserCreated(int UserId);

    // Test user model
    public record User(int Id, string Name);

    // Handlers
    public class UserHandler
    {
        public Task<User> HandleAsync(GetUser query, CancellationToken ct)
            => Task.FromResult(new User(query.Id, "Test User"));

        public Task<User> HandleAsync(CreateUser command, CancellationToken ct)
            => Task.FromResult(new User(1, command.Name));

        public Task<List<User>> HandleAsync(FindUsers query, CancellationToken ct)
            => Task.FromResult(new List<User> { new(1, "User 1"), new(2, "User 2") });

        public Task<(User, UserCreated)> HandleAsync(CreateUserWithEvent command, CancellationToken ct)
        {
            var user = new User(1, command.Name);
            return Task.FromResult((user, new UserCreated(user.Id)));
        }
    }

    // Sync handler
    public record GetUserSync(int Id) : IRequest<User>;

    public class SyncUserHandler
    {
        public User Handle(GetUserSync query) => new(query.Id, "Sync User");
    }

    [Fact]
    public async Task InvokeAsync_WithIRequest_InfersReturnType()
    {
        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<UserHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Type inference works - no need to specify User explicitly
        User user = await mediator.InvokeAsync(new GetUser(123), TestCancellationToken);

        Assert.NotNull(user);
        Assert.Equal(123, user.Id);
        Assert.Equal("Test User", user.Name);
    }

    [Fact]
    public async Task InvokeAsync_WithICommand_InfersReturnType()
    {
        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<UserHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // ICommand<T> inherits from IRequest<T>, so type inference works
        User user = await mediator.InvokeAsync(new CreateUser("John"), TestCancellationToken);

        Assert.NotNull(user);
        Assert.Equal("John", user.Name);
    }

    [Fact]
    public async Task InvokeAsync_WithIQuery_InfersReturnType()
    {
        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<UserHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // IQuery<T> inherits from IRequest<T>, so type inference works
        List<User> users = await mediator.InvokeAsync(new FindUsers("test"), TestCancellationToken);

        Assert.NotNull(users);
        Assert.Equal(2, users.Count);
    }

    [Fact]
    public async Task InvokeAsync_WithIRequest_AndCascadingMessage_InfersReturnType()
    {
        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<UserHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Even with tuple returns for cascading, type inference works
        User user = await mediator.InvokeAsync(new CreateUserWithEvent("Jane"), TestCancellationToken);

        Assert.NotNull(user);
        Assert.Equal("Jane", user.Name);
    }

    [Fact]
    public void Invoke_WithIRequest_InfersReturnType()
    {
        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<SyncUserHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Sync invoke also supports type inference
        User user = mediator.Invoke(new GetUserSync(456), TestContext.Current.CancellationToken);

        Assert.NotNull(user);
        Assert.Equal(456, user.Id);
        Assert.Equal("Sync User", user.Name);
    }

    [Fact]
    public async Task InvokeAsync_WithIRequest_CanStillUseExplicitGeneric()
    {
        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<UserHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Explicit generic syntax still works
        var user = await mediator.InvokeAsync<User>(new GetUser(789), TestCancellationToken);

        Assert.NotNull(user);
        Assert.Equal(789, user.Id);
    }

    [Fact]
    public async Task InvokeAsync_WithObjectOverload_StillWorks()
    {
        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<UserHandler>());

        using var provider = services.BuildServiceProvider();
        var mediator = provider.GetRequiredService<IMediator>();

        // Object overload still works for messages that don't implement IRequest<T>
        object message = new GetUser(999);
        var user = await mediator.InvokeAsync<User>(message, TestCancellationToken);

        Assert.NotNull(user);
        Assert.Equal(999, user.Id);
    }
}
