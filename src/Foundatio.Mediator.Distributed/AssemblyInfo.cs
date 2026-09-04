using Foundatio.Mediator;

// The library only publishes object-typed notifications on the worker's behalf, so call-site interception
// buys nothing here; the generator still emits the module and the administration handlers.
[assembly: MediatorConfiguration(DisableInterceptors = true)]
