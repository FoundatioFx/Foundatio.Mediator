#nullable enable
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Foundatio.Mediator
{
    public static partial class ServiceCollectionExtensions
    {
        public static IServiceCollection AddMediator(this IServiceCollection services)
        {
            services.AddSingleton<IMediator, Mediator>();

            // Register HandlerRegistration instances keyed by message type name
            // Note: Handlers themselves are NOT auto-registered in DI
            // Users can register them manually if they want specific lifetimes

            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.AsyncCallSyncHandlerTestMessage",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.AsyncCallSyncHandlerTestMessage",
                    AsyncCallSyncHandlerTestHandler_Handle_AsyncCallSyncHandlerTestMessage_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.CallSiteTestCommand",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.CallSiteTestCommand",
                    CallSiteTestCommandHandler_HandleAsync_CallSiteTestCommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.CallSiteTestNotification",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.CallSiteTestNotification",
                    CallSiteTestNotification1Handler_HandleAsync_CallSiteTestNotification_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.CallSiteTestNotification",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.CallSiteTestNotification",
                    CallSiteTestNotification2Handler_HandleAsync_CallSiteTestNotification_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.DebugUniqueMessage",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.DebugUniqueMessage",
                    (mediator, message, cancellationToken, responseType) => new ValueTask<object>(DebugUniqueMessageHandler_Handle_DebugUniqueMessage_StaticWrapper.UntypedHandle(mediator, message, cancellationToken, responseType)),
                    DebugUniqueMessageHandler_Handle_DebugUniqueMessage_StaticWrapper.UntypedHandle,
                    false));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.TestCommandWithDependencies",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.TestCommandWithDependencies",
                    TestCommandWithDependenciesHandler_HandleAsync_TestCommandWithDependencies_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.TestQueryWithDependencies",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.TestQueryWithDependencies",
                    TestQueryWithDependenciesHandler_HandleAsync_TestQueryWithDependencies_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.SimpleTestCommand",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.SimpleTestCommand",
                    SimpleTestCommandHandler_HandleAsync_SimpleTestCommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.DiagnosticTestMessage",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.DiagnosticTestMessage",
                    DiagnosticTestHandler_HandleAsync_DiagnosticTestMessage_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.FixedSyncCommand",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.FixedSyncCommand",
                    (mediator, message, cancellationToken, responseType) => new ValueTask<object>(FixedSyncCommand1Handler_Handle_FixedSyncCommand_StaticWrapper.UntypedHandle(mediator, message, cancellationToken, responseType)),
                    FixedSyncCommand1Handler_Handle_FixedSyncCommand_StaticWrapper.UntypedHandle,
                    false));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.FixedSyncCommand",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.FixedSyncCommand",
                    (mediator, message, cancellationToken, responseType) => new ValueTask<object>(FixedSyncCommand2Handler_Handle_FixedSyncCommand_StaticWrapper.UntypedHandle(mediator, message, cancellationToken, responseType)),
                    FixedSyncCommand2Handler_Handle_FixedSyncCommand_StaticWrapper.UntypedHandle,
                    false));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.FixedAsyncNotification",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.FixedAsyncNotification",
                    FixedAsync1Handler_HandleAsync_FixedAsyncNotification_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.FixedAsyncNotification",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.FixedAsyncNotification",
                    FixedAsync2Handler_HandleAsync_FixedAsyncNotification_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.ValidMethodMessage",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.ValidMethodMessage",
                    PartiallyIgnoredHandler_HandleAsync_ValidMethodMessage_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.GenericMessage<string>",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.GenericMessage<string>",
                    GenericMessageHandler_HandleAsync_GenericMessage_string__StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.ConcreteMessage",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.ConcreteMessage",
                    ConcreteMessageHandler_HandleAsync_ConcreteMessage_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.InterfaceTestMessage",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.InterfaceTestMessage",
                    InterfaceTestMessageHandler_HandleAsync_InterfaceTestMessage_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.InterfaceTestQuery",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.InterfaceTestQuery",
                    InterfaceTestQueryHandler_HandleAsync_InterfaceTestQuery_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.ComprehensiveCommand",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.ComprehensiveCommand",
                    ComprehensiveCommandHandler_HandleAsync_ComprehensiveCommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.ComprehensiveStringQuery",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.ComprehensiveStringQuery",
                    ComprehensiveStringQueryHandler_HandleAsync_ComprehensiveStringQuery_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.ComprehensiveIntQuery",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.ComprehensiveIntQuery",
                    ComprehensiveIntQueryHandler_HandleAsync_ComprehensiveIntQuery_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.ComprehensiveDICommand",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.ComprehensiveDICommand",
                    ComprehensiveDICommandHandler_HandleAsync_ComprehensiveDICommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.InterceptorTestCommand",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.InterceptorTestCommand",
                    InterceptorTestCommandHandler_HandleAsync_InterceptorTestCommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.TestMessageForMassTransitExclusion",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.TestMessageForMassTransitExclusion",
                    TestMessageForMassTransitExclusionHandler_HandleAsync_TestMessageForMassTransitExclusion_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.TestCommand",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.TestCommand",
                    TestCommandHandler_HandleAsync_TestCommand_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.TestQuery",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.TestQuery",
                    TestQueryHandler_HandleAsync_TestQuery_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.TestNotification",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.TestNotification",
                    TestNotificationHandler_HandleAsync_TestNotification_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.SimpleTestNotification",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.SimpleTestNotification",
                    SimpleTestNotificationHandler_HandleAsync_SimpleTestNotification_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.RegistrationTestMessage",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.RegistrationTestMessage",
                    RegistrationTestMessageHandler_HandleAsync_RegistrationTestMessage_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.RegistrationTestQuery",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.RegistrationTestQuery",
                    RegistrationTestQueryHandler_HandleAsync_RegistrationTestQuery_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.RegistrationTestQueryInt",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.RegistrationTestQueryInt",
                    RegistrationTestQueryIntHandler_HandleAsync_RegistrationTestQueryInt_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.PublishNotification",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.PublishNotification",
                    SinglePublishNotificationHandler_HandleAsync_PublishNotification_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.StaticHandlerTest.StaticSyncCommandMessage",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.StaticHandlerTest.StaticSyncCommandMessage",
                    (mediator, message, cancellationToken, responseType) => new ValueTask<object>(StaticTestHandler_Handle_StaticSyncCommandMessage_StaticWrapper.UntypedHandle(mediator, message, cancellationToken, responseType)),
                    StaticTestHandler_Handle_StaticSyncCommandMessage_StaticWrapper.UntypedHandle,
                    false));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.StaticHandlerTest.StaticAsyncCommandMessage",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.StaticHandlerTest.StaticAsyncCommandMessage",
                    StaticTestHandler_HandleAsync_StaticAsyncCommandMessage_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.StaticHandlerTest.StaticSyncQueryMessage",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.StaticHandlerTest.StaticSyncQueryMessage",
                    (mediator, message, cancellationToken, responseType) => new ValueTask<object>(StaticTestHandler_Handle_StaticSyncQueryMessage_StaticWrapper.UntypedHandle(mediator, message, cancellationToken, responseType)),
                    StaticTestHandler_Handle_StaticSyncQueryMessage_StaticWrapper.UntypedHandle,
                    false));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.StaticHandlerTest.StaticAsyncQueryMessage",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.StaticHandlerTest.StaticAsyncQueryMessage",
                    StaticTestHandler_HandleAsync_StaticAsyncQueryMessage_StaticWrapper.UntypedHandleAsync,
                    null,
                    true));
            services.AddKeyedSingleton<HandlerRegistration>("Foundatio.Mediator.Tests.StaticHandlerTest.StaticQueryWithDIMessage",
                new HandlerRegistration(
                    "Foundatio.Mediator.Tests.StaticHandlerTest.StaticQueryWithDIMessage",
                    (mediator, message, cancellationToken, responseType) => new ValueTask<object>(StaticTestHandler_Handle_StaticQueryWithDIMessage_StaticWrapper.UntypedHandle(mediator, message, cancellationToken, responseType)),
                    StaticTestHandler_Handle_StaticQueryWithDIMessage_StaticWrapper.UntypedHandle,
                    false));

            return services;
        }
    }
}
