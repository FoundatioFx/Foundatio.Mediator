![Foundatio](https://raw.githubusercontent.com/FoundatioFx/Foundatio/master/media/foundatio-dark-bg.svg#gh-dark-mode-only "Foundatio")![Foundatio](https://raw.githubusercontent.com/FoundatioFx/Foundatio/master/media/foundatio.svg#gh-light-mode-only "Foundatio")

[![Build status](https://github.com/FoundatioFx/Foundatio.Mediator/workflows/Build/badge.svg)](https://github.com/FoundatioFx/Foundatio.Mediator/actions)
[![NuGet Version](http://img.shields.io/nuget/v/Foundatio.Mediator.svg?style=flat)](https://www.nuget.org/packages/Foundatio.Mediator/)
[![feedz.io](https://img.shields.io/endpoint?url=https%3A%2F%2Ff.feedz.io%2Ffoundatio%2Ffoundatio%2Fshield%2FFoundatio.Mediator%2Flatest)](https://f.feedz.io/foundatio/foundatio/packages/Foundatio.Mediator/latest/download)
[![Discord](https://img.shields.io/discord/715744504891703319)](https://discord.gg/6HxgFCx)

Foundatio Mediator is a high-performance mediator library for .NET that uses source generators and C# interceptors to achieve near-direct-call performance with zero runtime reflection. Build completely message-oriented, loosely coupled apps that are easy to test — with zero boilerplate.

## ✨ Features

- 🚀 **Near-direct call performance** — source generators and interceptors eliminate runtime reflection
- ⚡ **Convention-based discovery** — handlers discovered by naming conventions, no interfaces or base classes required
- 🧩 **Plain handler classes** — sync or async, static or instance methods, any signature, multiple handlers per class
- 🌐 **Auto-generated API endpoints** — Minimal API endpoints generated from handlers with route, method, and parameter binding inference
- 📡 **Streaming handlers** — real-time Server-Sent Events on your API with just a handler method returning `IAsyncEnumerable<T>`
- 🎯 **Built-in Result\<T>** — rich status handling without exceptions, auto-mapped to HTTP status codes
- 🎪 **Middleware pipeline** — Before/After/Finally/Execute hooks with state passing and short-circuiting
- 🔄 **Cascading messages** — tuple returns automatically publish follow-on events
- 🔧 **Full DI support** — constructor and method parameter injection via Microsoft.Extensions.DependencyInjection
- 🔐 **Authorization** — built-in attribute-based authorization with policy support
- 🔒 **Compile-time safety** — analyzer diagnostics catch misconfigurations before runtime
- 🧪 **Easy testing** — plain objects with no framework coupling
- 🐛 **Superior debugging** — short, readable call stacks

## 🚀 Get Started

```bash
dotnet add package Foundatio.Mediator
```

**👉 [Getting Started Guide](https://mediator.foundatio.dev/guide/getting-started.html)** — step-by-step setup with code samples for ASP.NET Core and console apps.

**📖 [Complete Documentation](https://mediator.foundatio.dev)**

## 📂 Sample Applications

Explore complete working examples:

- **[Console Sample](samples/ConsoleSample/)** - Simple command-line application demonstrating handlers, middleware, and cascading messages
- **[Clean Architecture Sample](samples/CleanArchitectureSample/)** - Modular monolith showcasing:
  - Clean Architecture layers with domain separation
  - Repository pattern for data access
  - Cross-module communication via mediator
  - Domain events for loose coupling
  - Auto-generated API endpoints
  - Shared middleware across modules

## 📦 CI Packages (Feedz)

Want the latest CI build before it hits NuGet? Add the Feedz source (read‑only public) and install the pre-release version:

```bash
dotnet nuget add source https://f.feedz.io/foundatio/foundatio/nuget -n foundatio-feedz
dotnet add package Foundatio.Mediator --prerelease
```

Or add to your `NuGet.config`:

```xml
<configuration>
    <packageSources>
        <add key="foundatio-feedz" value="https://f.feedz.io/foundatio/foundatio/nuget" />
    </packageSources>
    <!-- Optional: limit this source to Foundatio packages -->
    <packageSourceMapping>
        <packageSource key="foundatio-feedz">
            <package pattern="Foundatio.*" />
        </packageSource>
    </packageSourceMapping>
</configuration>
```

CI builds are published with pre-release version tags (e.g. `1.0.0-alpha.12345+sha.abcdef`). Use them to try new features early—avoid in production unless you understand the changes.

## 🤝 Contributing

Contributions are welcome! Please feel free to submit a Pull Request. See our [documentation](https://mediator.foundatio.dev) for development guidelines.

## 🔗 Related Projects

[**@martinothamar/Mediator**](https://github.com/martinothamar/Mediator) was the primary source of inspiration for this library, but we wanted to use source interceptors and be conventional rather than requiring interfaces or base classes.

Other mediator and messaging libraries for .NET:

- **[MediatR](https://github.com/jbogard/MediatR)** - Simple, unambitious mediator implementation in .NET with request/response and notification patterns
- **[MassTransit](https://github.com/MassTransit/MassTransit)** - Distributed application framework for .NET with in-process mediator capabilities alongside service bus features
- **[Immediate.Handlers](https://github.com/ImmediatePlatform/Immediate.Handlers)** - another implementation of the mediator pattern in .NET using source-generation.

## 📄 License

Apache-2.0 License
