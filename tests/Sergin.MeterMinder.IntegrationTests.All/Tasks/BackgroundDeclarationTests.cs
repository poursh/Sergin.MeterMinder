using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Tasks;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

public sealed class BackgroundDeclarationTests
{
    private static ModuleConfigurationSource[] Sources(params Type[] types) =>
        [ModuleConfigurationSource.FromTypesInSchema("tst", [typeof(ScheduleProbeAggregateFeatureConfiguration), .. types])];

    private static string Refusal(Type configuration) =>
        Assert.Throws<InvalidOperationException>(() => CommandConfigurationRegistry.FromSources(Sources(configuration))).Message;

    [Fact]
    public void Background_IsReadIntoSettingsAndCatalog()
    {
        ModuleConfigurationSource[] sources = Sources(typeof(BackgroundProbeCommandConfiguration));
        var registry = CommandConfigurationRegistry.FromSources(sources);
        var catalog = TaskCatalog.FromSources(sources, registry);

        BackgroundSettings? settings = registry.For(typeof(BackgroundProbeCommand)).Background;

        Assert.Equal(TimeSpan.FromMinutes(2), settings!.Timeout);
        Assert.Equal(settings, catalog.For(typeof(BackgroundProbeCommand))!.Background);
    }

    [Fact]
    public void Background_WithoutSchedulable_RefusesNamingTheRequest()
    {
        string message = Refusal(typeof(BackgroundOnlyProbeCommandConfiguration));

        Assert.Contains(typeof(BackgroundProbeCommand).FullName!, message);
        Assert.Contains("Schedulable(", message);
    }

    [Theory]
    [InlineData(typeof(BackgroundZeroProbeCommandConfiguration))]
    [InlineData(typeof(BackgroundNegativeProbeCommandConfiguration))]
    public void Background_ZeroOrNegativeTimeout_Refuses(Type configuration)
    {
        string message = Refusal(configuration);

        Assert.Contains(typeof(BackgroundProbeCommand).FullName!, message);
        Assert.Contains("timeout", message);
    }

    [Fact]
    public void Background_TimeoutAboveOneHour_Refuses()
    {
        string message = Refusal(typeof(BackgroundTooLongProbeCommandConfiguration));

        Assert.Contains(typeof(BackgroundProbeCommand).FullName!, message);
        Assert.Contains("1 hour", message);
    }

    [Theory]
    [InlineData(typeof(BackgroundSetPropertyProbeCommandConfiguration), typeof(BackgroundSetPropertyProbeCommand))]
    [InlineData(typeof(BackgroundChildTransitionProbeCommandConfiguration), typeof(BackgroundChildTransitionProbeCommand))]
    [InlineData(typeof(BackgroundDeleteProbeCommandConfiguration), typeof(BackgroundDeleteProbeCommand))]
    public void Background_OnAnythingButARootTransition_RefusesNamingTheRequest(Type configuration, Type request)
    {
        string message = Refusal(configuration);

        Assert.Contains(request.FullName!, message);
        Assert.Contains("ITransitionCommand", message);
    }

    [Fact]
    public void NoBackground_LeavesSettingNull()
    {
        var registry = CommandConfigurationRegistry.FromSources(Sources(typeof(TouchProbeCommandConfiguration)));

        Assert.Null(registry.For(typeof(TouchProbeCommand)).Background);
    }
}
