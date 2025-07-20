# ConsoleSample - Comprehensive Foundatio.Mediator Demonstration

This comprehensive sample demonstrates all the key features of the Foundatio.Mediator library in a single application. It combines functionality that was previously split across multiple sample projects.

## �️ Project Structure

The sample is organized into logical folders for better maintainability:

```
ConsoleSample/
├── Program.cs                          # Application entry point
├── SampleRunner.cs                     # Orchestrates sample demonstrations
├── ServiceConfiguration.cs             # Dependency injection setup
├── Messages/                           # Message definitions organized by feature
│   ├── SimpleMessages.cs              # Basic command/query messages
│   ├── DependencyInjectionMessages.cs # Messages for DI examples
│   ├── OrderMessages.cs               # Order-related events and commands
│   └── CalculationMessages.cs         # Sync/async calculation messages
├── Handlers/                           # Handler implementations organized by feature
│   ├── SimpleHandlers.cs              # Basic command/query handlers
│   ├── DependencyInjectionHandlers.cs # Handlers with DI examples
│   ├── OrderHandlers.cs               # Order processing handlers
│   └── CalculationHandlers.cs         # Sync/async calculation handlers
└── Services/                           # Supporting services for DI examples
    ├── EmailService.cs                 # Email service implementation
    ├── GreetingService.cs              # Greeting service implementation
    ├── NotificationServices.cs         # Email/SMS notification services
    └── AuditService.cs                 # Audit logging service
```

## �🎯 What This Sample Demonstrates

### 1. Simple Command and Query Pattern
- **PingCommand**: A simple command with no response (fire-and-forget)
- **GreetingQuery**: A query that returns a string response
- Demonstrates basic `InvokeAsync()` and `Invoke<T>()` usage

### 2. Dependency Injection in Handlers
- **SendWelcomeEmailCommand**: Shows how handler methods can inject services via parameters
- **CreatePersonalizedGreetingQuery**: Demonstrates both service injection and logging
- Shows how the mediator automatically resolves dependencies from the DI container

### 3. Publish Pattern (Multiple Handlers)
- **OrderCreatedEvent**: A single event handled by multiple handlers
  - `OrderEmailNotificationHandler`: Sends email notifications
  - `OrderSmsNotificationHandler`: Sends SMS notifications  
  - `OrderAuditHandler`: Logs the event for auditing
- Demonstrates `PublishAsync()` calling all registered handlers for an event

### 4. Single Handler Invoke Pattern
- **ProcessOrderCommand**: A command with exactly one handler that returns a result
- Shows how `InvokeAsync<T>()` works with single handlers
- Demonstrates the difference between Invoke (single handler) and Publish (multiple handlers)

### 5. Mixed Sync/Async Handlers
- **SyncCalculationQuery**: Shows synchronous handler methods
- **AsyncCalculationQuery**: Shows asynchronous handler methods
- Demonstrates that the mediator can handle both sync and async handlers seamlessly

## 🏗️ Architecture Patterns Demonstrated

### Handler Discovery Convention
All handlers follow the naming convention:
- Class names end with `Handler` or `Consumer`
- Method names: `Handle`, `Handles`, `HandleAsync`, `HandlesAsync`, `Consume`, `Consumes`, `ConsumeAsync`, `ConsumesAsync`

### Dependency Injection Patterns
- **Constructor Injection**: Services injected into service classes
- **Method Parameter Injection**: Services injected directly into handler methods
- **Mixed Injection**: Handlers can use both patterns simultaneously

### Message Types
- **Commands**: Actions to be performed (PingCommand, SendWelcomeEmailCommand)
- **Queries**: Requests for data (GreetingQuery, CreatePersonalizedGreetingQuery) 
- **Events**: Notifications of something that happened (OrderCreatedEvent)

## 🚀 Running the Sample

```bash
dotnet run
```

## 📝 Sample Output

The application will demonstrate each pattern in sequence:

```
🚀 Foundatio.Mediator Comprehensive Sample
==========================================

1️⃣ Testing Simple Command and Query...
Ping 123 received!
Greeting response: Hello, World!
✅ Simple command/query test completed!

2️⃣ Testing Dependency Injection in Handlers...
✉️ Email sent to john@example.com: Welcome!
Personalized greeting: Hello Alice, welcome to our amazing application!
✅ Dependency injection test completed!

3️⃣ Testing Publish with Multiple Handlers...
📢 Publishing OrderCreatedEvent (should trigger multiple handlers)...
📋 Audit logged: OrderCreated - OrderCreatedEvent { ... }
📱 SMS notification sent: Your order ORD-001 is confirmed!
📧 Email notification sent: Order ORD-001 has been created for $299.99
✅ Publish completed - all handlers were called!

4️⃣ Testing Invoke with Single Handler...
🔄 Processing order ORD-001 with type: VIP Processing
Process result: Order ORD-001 processed successfully with VIP Processing
✅ Single handler invoke test completed!

5️⃣ Testing Mixed Sync/Async Handlers...
🧮 Sync calculation: 10 + 5 = 15
Sync calculation result: Sum: 15
🧮 Async calculation: 20 * 3 = 60
Async calculation result: Product: 60
✅ Mixed sync/async test completed!

🎉 All samples completed successfully!
```

## 🔧 Key Features Showcased

- ✅ **Convention-based handler discovery** - No interfaces or base classes required
- ✅ **Full dependency injection support** - Both constructor and method parameter injection
- ✅ **Publish/Subscribe pattern** - Multiple handlers for a single event
- ✅ **Request/Response pattern** - Single handler with return values
- ✅ **Mixed sync/async handlers** - Support for both synchronous and asynchronous operations
- ✅ **Compile-time validation** - Source generator ensures correct handler signatures
- ✅ **Logging integration** - Built-in support for `ILogger<T>`
- ✅ **Cancellation token support** - Automatic injection of cancellation tokens

## 📚 Learning Path

1. **Start with simple commands/queries** to understand basic patterns
2. **Add dependency injection** to see how services are resolved
3. **Explore publish patterns** to understand event-driven architecture
4. **Mix sync and async** handlers to see flexibility
5. **Review generated code** in the `Generated/` folder to understand how it works

This sample provides a complete overview of Foundatio.Mediator capabilities in a single, easy-to-understand application.
