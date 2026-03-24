using Microsoft.Extensions.DependencyInjection;

namespace Foundatio.Mediator.Tests.Integration;

public class E2E_HandlerMetadataTests
{
    public record MetadataCommand(string Value) : ICommand<string>;

    [System.ComponentModel.Description("type-description")]
    public class MetadataCommandHandler
    {
        [System.ComponentModel.Description("method-description")]
        public string Handle(MetadataCommand command)
        {
            return command.Value;
        }
    }

    [Fact]
    public void Registry_Exposes_HandlerMethod_And_AttributeMetadata()
    {
        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<MetadataCommandHandler>());

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<HandlerRegistry>();

        var registration = registry.GetRegistrationsForMessageType(typeof(MetadataCommand)).Single();

        Assert.Contains("MetadataCommandHandler", registration.DescriptorId);
        Assert.Contains("MetadataCommand", registration.DescriptorId);
        Assert.EndsWith("_Handler", registration.DescriptorId, StringComparison.Ordinal);

        Assert.NotNull(registration.SourceHandlerType);
        Assert.Equal(typeof(MetadataCommandHandler), registration.SourceHandlerType);

        Assert.NotNull(registration.HandlerMethod);
        Assert.Equal(nameof(MetadataCommandHandler.Handle), registration.HandlerMethod!.Name);
        Assert.Equal(typeof(MetadataCommand), registration.HandlerMethod.GetParameters().Single().ParameterType);

        var metadata = registration.GetAttributes<System.ComponentModel.DescriptionAttribute>();
        Assert.Equal(2, metadata.Count);
        Assert.Contains(metadata, m => m.Target == HandlerAttributeTarget.HandlerType);
        Assert.Contains(metadata, m => m.Target == HandlerAttributeTarget.HandlerMethod);
    }

    [Fact]
    public void PreferredAttribute_Resolves_StronglyTyped_Attribute_Instance()
    {
        var services = new ServiceCollection();
        services.AddMediator(b => b.AddAssembly<MetadataCommandHandler>());

        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<HandlerRegistry>();

        var handlers = registry.GetHandlersWithAttribute<System.ComponentModel.DescriptionAttribute>();
        var registration = Assert.Single(handlers);

        var preferred = registration.GetPreferredAttribute<System.ComponentModel.DescriptionAttribute>();
        Assert.NotNull(preferred);
        Assert.Equal(HandlerAttributeTarget.HandlerMethod, preferred!.Target);

        var reflected = preferred.Attribute as System.ComponentModel.DescriptionAttribute;
        Assert.NotNull(reflected);
        Assert.Equal("method-description", reflected!.Description);

        var typeLevel = registration
            .GetAttributes<System.ComponentModel.DescriptionAttribute>()
            .Single(m => m.Target == HandlerAttributeTarget.HandlerType);

        var typeLevelReflected = typeLevel.Attribute as System.ComponentModel.DescriptionAttribute;
        Assert.NotNull(typeLevelReflected);
        Assert.Equal("type-description", typeLevelReflected!.Description);
    }
}
