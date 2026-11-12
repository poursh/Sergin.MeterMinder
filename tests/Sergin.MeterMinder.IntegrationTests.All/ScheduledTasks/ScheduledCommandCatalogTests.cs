using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.ScheduledTasks;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

public sealed class ScheduledCommandCatalogTests
{
    private static readonly Type[] reusable =
    [
        typeof(ScheduleProbeAggregateFeatureConfiguration),
        typeof(TouchProbeCommandConfiguration),
        typeof(TouchProbeChildCommandConfiguration),
        typeof(TouchOneProbeCommandConfiguration),
        typeof(UnconfiguredProbeCommand),
    ];

    private static ScheduledCommandCatalog Build(params Type[] types)
    {
        ModuleConfigurationSource[] sources = [ModuleConfigurationSource.FromTypesInSchema("tst", types)];
        return ScheduledCommandCatalog.FromSources(sources, CommandConfigurationRegistry.FromSources(sources));
    }

    private static string Refusal(params Type[] types) =>
        Assert.Throws<InvalidOperationException>(() => Build([typeof(ScheduleProbeAggregateFeatureConfiguration), .. types])).Message;

    [Fact]
    public void Schedulable_IsInTheCatalogue()
    {
        ScheduledCommandCatalog catalog = Build(reusable);

        Assert.True(catalog.TryGet("tst.probes.touch", out ScheduledCommand? command));
        Assert.Equal(typeof(TouchProbeCommand), command.RequestType);
        Assert.Equal("tst", command.Schema);
        Assert.Equal(typeof(ScheduleProbe), command.Root);
        Assert.Equal(CommandTargetKind.Entity, command.TargetKind);
        Assert.True(command.Selectable);
        Assert.Equal(typeof(ScheduleProbeId), command.IdType);
        Assert.Null(command.RootIdType);
        Assert.Same(command, catalog.For(typeof(TouchProbeCommand)));
        Assert.Equal(3, catalog.Commands.Count);
    }

    [Fact]
    public void ChildCommand_CarriesRootIdType()
    {
        Assert.True(Build(reusable).TryGet("tst.probes.touch-child", out ScheduledCommand? command));
        Assert.Equal(CommandTargetKind.ChildEntity, command.TargetKind);
        Assert.Equal(typeof(ScheduleProbeId), command.RootIdType);
        Assert.Equal(typeof(ScheduleProbeChildId), command.IdType);
    }

    [Fact]
    public void NonSelectable_IsNotSelectable()
    {
        Assert.True(Build(reusable).TryGet("tst.probes.touch-one", out ScheduledCommand? command));
        Assert.False(command.Selectable);
    }

    [Fact]
    public void Unconfigured_IsNotSchedulable()
    {
        ScheduledCommandCatalog catalog = Build(reusable);

        Assert.Null(catalog.For(typeof(UnconfiguredProbeCommand)));
        Assert.False(catalog.TryGet("tst.probes.nothing", out _));
    }

    [Fact]
    public void VersionGuarded_IsRefused()
    {
        string message = Refusal(typeof(VersionedProbeAggregateFeatureConfiguration), typeof(GuardedProbeCommandConfiguration));
        Assert.Contains(typeof(GuardedProbeCommand).FullName!, message);
    }

    [Fact]
    public void VersionGuardedWithWaiver_IsAllowed()
    {
        ScheduledCommandCatalog catalog = Build(
            typeof(VersionedProbeAggregateFeatureConfiguration), typeof(WaivedGuardedProbeCommandConfiguration));

        Assert.True(catalog.TryGet("tst.guarded.touch", out _));
    }

    [Fact]
    public void Creation_IsRefused() =>
        Assert.Contains(typeof(CreateProbeCommand).FullName!, Refusal(typeof(CreateProbeCommandConfiguration)));

    [Fact]
    public void SystemCommand_IsRefused() =>
        Assert.Contains(typeof(SystemProbeCommand).FullName!, Refusal(typeof(SystemProbeCommandConfiguration)));

    [Fact]
    public void Untyped_IsRefused() =>
        Assert.Contains(typeof(UntypedProbeCommand).FullName!, Refusal(typeof(UntypedProbeCommandConfiguration)));

    [Fact]
    public void MalformedName_IsRefused() =>
        Assert.Contains(typeof(MalformedNameCommand).FullName!, Refusal(typeof(MalformedNameCommandConfiguration)));

    [Fact]
    public void DuplicateName_IsRefused()
    {
        string message = Refusal(typeof(DuplicateNameOneCommandConfiguration), typeof(DuplicateNameTwoCommandConfiguration));

        Assert.Contains(typeof(DuplicateNameOneCommand).FullName!, message);
        Assert.Contains(typeof(DuplicateNameTwoCommand).FullName!, message);
    }

    [Fact]
    public void NameOutsideSchema_IsRefused() =>
        Assert.Contains(typeof(OutsideSchemaCommand).FullName!, Refusal(typeof(OutsideSchemaCommandConfiguration)));

    [Theory]
    [InlineData("dm.devices.read-profile", true)]
    [InlineData("tst.probes.touch", true)]
    [InlineData("a.b", true)]
    [InlineData("Tst.Bad", false)]
    [InlineData("tst", false)]
    [InlineData("tst.", false)]
    [InlineData("tst.a-", false)]
    [InlineData("1st.a", false)]
    [InlineData("", false)]
    public void Names_AreValidated(string name, bool valid) => Assert.Equal(valid, ScheduledCommandNames.IsValid(name));
}
