---
layout: home

hero:
  name: Foundatio Mediator
  text: Blazingly Fast C# Mediator
  tagline: Convention-based mediator powered by source generators and interceptors
  image:
    src: https://raw.githubusercontent.com/FoundatioFx/Foundatio/main/media/foundatio-icon.png
    alt: Foundatio Mediator
  actions:
    - theme: brand
      text: Get Started
      link: /guide/getting-started
    - theme: alt
      text: View on GitHub
      link: https://github.com/FoundatioFx/Foundatio.Mediator

features:
  - icon: ⚡
    title: Near-Direct Call Performance
    details: Zero runtime reflection with source generators and C# interceptors for blazing fast execution.
    link: /guide/performance
  - icon: 🎯
    title: Convention-Based Discovery
    details: No interfaces or base classes required. Just name your classes and methods following simple conventions.
    link: /guide/handler-conventions
  - icon: 🧩
    title: Plain Handler Classes
    details: Use regular classes or static methods. Sync or async, any signature, multiple handlers per class. No framework coupling.
    link: /guide/handler-conventions
  - icon: 🌐
    title: Auto-Generated API Endpoints
    details: Full Minimal API endpoints generated from your handlers — routes, methods, parameter binding, OpenAPI metadata, and authorization all inferred automatically. No boilerplate.
    link: /guide/endpoints
  - icon: 📡
    title: Real-Time Streaming
    details: Add Server-Sent Events to your API by returning IAsyncEnumerable<T> from a handler. Real-time push with zero infrastructure plumbing.
    link: /guide/streaming-handlers
  - icon: 🎯
    title: Rich Result Types
    details: Built-in Result<T> for success, validation errors, and failure states — auto-mapped to HTTP status codes on endpoints.
    link: /guide/result-types
  - icon: 🔧
    title: Full Dependency Injection
    details: Built-in support for Microsoft.Extensions.DependencyInjection with constructor and method injection.
    link: /guide/dependency-injection
  - icon: 🎪
    title: Middleware Pipeline
    details: Before/After/Finally/Execute hooks with state passing and short-circuiting capabilities.
    link: /guide/middleware
  - icon: 🔄
    title: Automatic Message Cascading
    details: Return tuples to automatically publish additional messages in sequence — ideal for event-driven workflows.
    link: /guide/cascading-messages
  - icon: 🔒
    title: Compile-Time Safety
    details: Comprehensive compile-time diagnostics and validation to catch errors early.
    link: /guide/troubleshooting
  - icon: 🧪
    title: Easy Testing
    details: Handlers are plain objects, making unit testing straightforward without framework mocking.
  - icon: 🐛
    title: Superior Debugging
    details: Short, simple call stacks with minimal indirection for excellent debugging experience.
---

## Quick Example

Create a simple handler by following naming conventions:

```csharp
public record Ping(string Text);

public static class PingHandler
{
    public static string Handle(Ping msg) => $"Pong: {msg.Text}";
}
```

Register the mediator and use it:

```csharp
// Program.cs
services.AddMediator();

// Usage
var reply = await mediator.InvokeAsync<string>(new Ping("Hello"));
// Output: "Pong: Hello"
```

Turn your message handlers into API endpoints automatically:

```csharp
app.MapMyAppEndpoints();
// That's it — routes, methods, and parameter binding are all generated for you.
```
