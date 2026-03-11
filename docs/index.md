---
layout: home

hero:
  name: Foundatio Mediator
  text: Build Loosely Coupled .NET Apps — Without the Tradeoffs
  tagline: Easy to maintain, easy to test, and blazingly fast — a convention-based mediator powered by source generators and interceptors
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
  - icon: 🧩
    title: Loosely Coupled by Default
    details: Every interaction flows through messages, so components never call each other directly. Change, replace, or remove any handler without ripple effects across your codebase.
    link: /guide/what-is-foundatio-mediator
  - icon: 🔗
    title: Compose with Events
    details: Publish an event and any number of handlers react — without knowing about each other. Add new behavior to your app without modifying existing code.
    link: /guide/events-and-notifications
  - icon: 🧪
    title: Easy to Test
    details: Handlers are plain classes with no framework base types. Unit test them like any other object — no mediator mocking, no DI container, no ceremony.
    link: /guide/testing
  - icon: ⚡
    title: No Performance Tax
    details: Source generators and C# interceptors compile your mediator calls into near-direct method calls. You get the architecture benefits without the runtime overhead.
    link: /guide/performance
  - icon: 🎯
    title: Convention-Based, Zero Boilerplate
    details: No interfaces, no base classes, no manual registration. Just name your classes and methods following simple conventions — the source generator handles all the wiring.
    link: /guide/handler-conventions
  - icon: 🌐
    title: Auto-Generated API Endpoints
    details: Full Minimal API endpoints generated from your handlers — routes, methods, parameter binding, OpenAPI metadata, and authorization all inferred automatically.
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
    title: Compile-Time Safety & Debugging
    details: Comprehensive diagnostics catch errors early. Short, simple call stacks with minimal indirection make debugging straightforward.
---

## Quick Example

Create a simple handler by following naming conventions:

```csharp
public record Ping(string Text);

public class PingHandler
{
    public string Handle(Ping msg) => $"Pong: {msg.Text}";
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
app.MapMediatorEndpoints();
// That's it — routes, methods, and parameter binding are all generated for you.
```
