using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.ReadProfile;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

/// <summary>Every task is in the catalogue, configured or not, by its derived or declared name; a bad name refuses host start.</summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class TaskCatalogTests(SerginWebApiFactory<Program> factory)
{
    private static readonly Type[] reusable =
    [
        typeof(ScheduleProbeAggregateFeatureConfiguration),
        typeof(TouchProbeCommandConfiguration),
        typeof(TouchOneProbeCommandConfiguration),
        typeof(UnconfiguredProbeCommand),
    ];

    private static TaskCatalog Build(params Type[] types)
    {
        ModuleConfigurationSource[] sources = [ModuleConfigurationSource.FromTypesInSchema("tst", types)];
        return TaskCatalog.FromSources(sources, CommandConfigurationRegistry.FromSources(sources));
    }

    private static string Refusal(params Type[] types) =>
        Assert.Throws<InvalidOperationException>(() => Build([typeof(ScheduleProbeAggregateFeatureConfiguration), .. types])).Message;

    [Fact]
    public void DerivesName()
    {
        TaskDescriptor? readProfile = factory.Services.GetRequiredService<TaskCatalog>().ByRequest(typeof(ReadDeviceProfileCommand));
        Assert.Equal("dm.device.read-device-profile", readProfile?.Name);

        TaskDescriptor? sync = Build(typeof(ScheduleProbeAggregateFeatureConfiguration), typeof(SyncProbesCommandConfiguration))
            .ByRequest(typeof(SyncProbesCommand));
        Assert.NotNull(sync);
        Assert.Equal("tst.schedule-probe.sync-probes", sync.Name);
        Assert.Equal(CommandTargetKind.Collection, sync.TargetKind);
        Assert.Null(sync.IdType);
        Assert.False(sync.Selectable);
        Assert.True(sync.Settings.Schedulable);
    }

    [Theory]
    [InlineData(typeof(ReadDeviceProfileCommand), typeof(Device), "dm", "dm.device.read-device-profile")]
    [InlineData(typeof(SyncProbesCommand), typeof(ScheduleProbe), "tst", "tst.schedule-probe.sync-probes")]
    [InlineData(typeof(UnconfiguredProbeCommand), typeof(HTTPProbe), "tst", "tst.http-probe.unconfigured-probe")]
    public void Derive_KebabCasesRootAndCommand(Type request, Type root, string? schema, string expected) =>
        Assert.Equal(expected, TaskNames.Derive(schema, root, request));

    [Fact]
    public void ConfiguredTask_IsInTheCatalogue()
    {
        TaskCatalog catalog = Build(reusable);

        Assert.True(catalog.TryGet("tst.probes.touch", out TaskDescriptor? command));
        Assert.Equal(typeof(TouchProbeCommand), command.RequestType);
        Assert.Equal("tst", command.Schema);
        Assert.Equal(typeof(ScheduleProbe), command.Root);
        Assert.Equal(CommandTargetKind.Entity, command.TargetKind);
        Assert.True(command.Selectable);
        Assert.Equal(typeof(ScheduleProbeId), command.IdType);
        Assert.True(command.Settings.Schedulable);
        Assert.Same(command, catalog.ByRequest(typeof(TouchProbeCommand)));
        Assert.Same(command, catalog.ByName("tst.probes.touch"));
        Assert.Equal(3, catalog.Commands.Count);
    }

    [Fact]
    public void NonSelectable_IsNotSelectable()
    {
        Assert.True(Build(reusable).TryGet("tst.probes.touch-one", out TaskDescriptor? command));
        Assert.False(command.Selectable);
    }

    [Fact]
    public void UnconfiguredTask_IsInTheCatalogue_NotSchedulable()
    {
        TaskCatalog catalog = Build(reusable);

        TaskDescriptor? command = catalog.ByRequest(typeof(UnconfiguredProbeCommand));
        Assert.NotNull(command);
        Assert.Equal("tst.schedule-probe.unconfigured-probe", command.Name);
        Assert.False(command.Settings.Schedulable);
        Assert.Null(catalog.ByName("tst.probes.nothing"));
    }

    [Fact]
    public void Refuses_MalformedName()
    {
        string message = Refusal(typeof(MalformedNameCommandConfiguration));

        Assert.Contains(typeof(MalformedNameCommand).FullName!, message, StringComparison.Ordinal);
        Assert.Contains("\"Tst.Bad\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_NameOutsideSchema()
    {
        string message = Refusal(typeof(OutsideSchemaCommandConfiguration));

        Assert.Contains(typeof(OutsideSchemaCommand).FullName!, message, StringComparison.Ordinal);
        Assert.Contains("\"tst.\"", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_DuplicateNames()
    {
        string message = Refusal(typeof(DuplicateNameOneCommandConfiguration), typeof(DuplicateNameTwoCommandConfiguration));

        Assert.Contains(typeof(DuplicateNameOneCommand).FullName!, message, StringComparison.Ordinal);
        Assert.Contains(typeof(DuplicateNameTwoCommand).FullName!, message, StringComparison.Ordinal);
        Assert.Contains("tst.schedule-probe.duplicate-name-one", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Refuses_DerivedNameInASourceWithNoSchema()
    {
        ModuleConfigurationSource[] sources =
            [ModuleConfigurationSource.FromTypes(typeof(ScheduleProbeAggregateFeatureConfiguration), typeof(SyncProbesCommandConfiguration))];

        string message = Assert.Throws<InvalidOperationException>(
            () => TaskCatalog.FromSources(sources, CommandConfigurationRegistry.FromSources(sources))).Message;

        Assert.Contains(typeof(SyncProbesCommand).FullName!, message, StringComparison.Ordinal);
        Assert.Contains("no schema", message, StringComparison.Ordinal);
        Assert.Contains("Name(...)", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DeclaredNameInASourceWithNoSchema_IsAccepted()
    {
        ModuleConfigurationSource[] sources =
            [ModuleConfigurationSource.FromTypes(typeof(ScheduleProbeAggregateFeatureConfiguration), typeof(TouchProbeCommandConfiguration))];

        var catalog = TaskCatalog.FromSources(sources, CommandConfigurationRegistry.FromSources(sources));

        Assert.Equal("tst.probes.touch", catalog.ByRequest(typeof(TouchProbeCommand))?.Name);
    }

    [Fact]
    public void Refuses_EveryBadNameAtOnce()
    {
        string message = Refusal(typeof(MalformedNameCommandConfiguration), typeof(OutsideSchemaCommandConfiguration));

        Assert.Contains(typeof(MalformedNameCommand).FullName!, message, StringComparison.Ordinal);
        Assert.Contains(typeof(OutsideSchemaCommand).FullName!, message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("dm.device.read-device-profile", true)]
    [InlineData("tst.probes.touch", true)]
    [InlineData("a.b", true)]
    [InlineData("Tst.Bad", false)]
    [InlineData("tst", false)]
    [InlineData("tst.", false)]
    [InlineData("tst.a-", false)]
    [InlineData("1st.a", false)]
    [InlineData("", false)]
    public void Names_AreValidated(string name, bool valid) => Assert.Equal(valid, TaskNames.IsValid(name));
}

/// <summary>A root whose name opens with an acronym, for the kebab-case rule.</summary>
#pragma warning disable S101 // The acronym is the point of the probe.
internal static class HTTPProbe;
#pragma warning restore S101
