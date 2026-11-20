using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Update;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>What <c>Task(...)</c> declares, what a task that declares nothing gets, and the timeouts host start refuses.</summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class TaskDeclarationTests(SerginWebApiFactory<Program> factory)
{
    private static CommandConfigurationRegistry Registry(params Type[] types) =>
        CommandConfigurationRegistry.FromSources(
            [ModuleConfigurationSource.FromTypesInSchema("tst", [typeof(ScheduleProbeAggregateFeatureConfiguration), .. types])]);

    private static string Refusal(Type configuration) =>
        Assert.Throws<InvalidOperationException>(() => Registry(configuration)).Message;

    [Fact]
    public void UnconfiguredTask_GetsEveryDefault()
    {
        TaskSettings? settings = Registry(typeof(UnconfiguredProbeCommand)).For(typeof(UnconfiguredProbeCommand)).Task;

        Assert.Equal(
            new TaskSettings("tst.schedule-probe.unconfigured-probe", TimeSpan.FromMinutes(5), MaxDuration: null, Cancelable: true, Schedulable: false),
            settings);
        Assert.Equal(TimeSpan.FromMinutes(5), TaskSettings.DefaultInactivityTimeout);
        Assert.Equal(TimeSpan.FromHours(1), TaskSettings.MaxInactivityTimeout);
    }

    [Fact]
    public void Task_DeclaresEachTrait()
    {
        TaskSettings? settings = Registry(typeof(DeclaredProbeCommandConfiguration)).For(typeof(DeclaredProbeCommand)).Task;

        Assert.Equal(
            new TaskSettings("tst.probes.old", TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10), Cancelable: false, Schedulable: true),
            settings);
    }

    [Theory]
    [InlineData(typeof(ZeroInactivityProbeCommandConfiguration))]
    [InlineData(typeof(LongInactivityProbeCommandConfiguration))]
    public void Refuses_InactivityTimeoutOutOfRange(Type configuration)
    {
        string message = Refusal(configuration);

        Assert.Contains(configuration.FullName!, message, StringComparison.Ordinal);
        Assert.Contains(typeof(DeclaredProbeCommand).FullName!, message, StringComparison.Ordinal);
        Assert.Contains("inactivity timeout", message, StringComparison.Ordinal);
        Assert.Contains("1 hour", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_InactivityTimeoutAboveOneHour_NamingTheLimit() =>
        Assert.Contains("61 minutes", Refusal(typeof(LongInactivityProbeCommandConfiguration)), StringComparison.Ordinal);

    [Fact]
    public void Refuses_MaxDurationBelowInactivity()
    {
        string message = Refusal(typeof(ShortMaxDurationProbeCommandConfiguration));

        Assert.Contains(typeof(ShortMaxDurationProbeCommandConfiguration).FullName!, message, StringComparison.Ordinal);
        Assert.Contains(typeof(DeclaredProbeCommand).FullName!, message, StringComparison.Ordinal);
        Assert.Contains("maximum duration of 5 minutes", message, StringComparison.Ordinal);
    }

    [Fact]
    public void NonTask_HasNoTaskSettings() =>
        Assert.Null(factory.Services.GetRequiredService<CommandConfigurationRegistry>().For(typeof(UpdateDeviceCommand)).Task);
}
