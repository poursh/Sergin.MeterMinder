using Sergin.MeterMinder.IntegrationTests.All.Tasks;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Hosts.Commands;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands.Types;

/// <summary>
/// The two task shapes: what <see cref="CommandTypes"/> reads from them, the version rule they imply, and the type
/// guard's refusals of a misshapen task.
/// </summary>
public sealed class TaskCommandTypeTests
{
    [Fact]
    public void EntityTask_TargetsItsRootEntity()
    {
        CommandTarget? target = CommandTypes.TargetOf(typeof(EntityTaskProbe));

        Assert.NotNull(target);
        Assert.Equal(typeof(ScheduleProbe), target.Root);
        Assert.Equal(CommandTargetKind.Entity, target.Kind);
        Assert.True(CommandTypes.IsTask(typeof(EntityTaskProbe)));
        Assert.Equal([typeof(ITaskCommand<,>)], CommandTypes.EffectsOf(typeof(EntityTaskProbe)));
    }

    [Fact]
    public void CollectionTask_TargetsTheCollection()
    {
        CommandTarget? target = CommandTypes.TargetOf(typeof(CollectionTaskProbe));

        Assert.NotNull(target);
        Assert.Equal(typeof(ScheduleProbe), target.Root);
        Assert.Equal(CommandTargetKind.Collection, target.Kind);
        Assert.True(CommandTypes.IsTask(typeof(CollectionTaskProbe)));
        Assert.Equal([typeof(ITaskCommand<>)], CommandTypes.EffectsOf(typeof(CollectionTaskProbe)));
    }

    [Fact]
    public void NonTask_IsNotATask() => Assert.False(CommandTypes.IsTask(typeof(GuardedProbeCommand)));

    [Fact]
    public void Task_OnVersionedRoot_RequiresNoExpectedVersion()
    {
        var registry = CommandConfigurationRegistry.FromSources(
            [ModuleConfigurationSource.FromTypes(typeof(VersionedTaskProbe), typeof(VersionedProbeAggregateFeatureConfiguration))]);

        CommandSettings settings = registry.For(typeof(VersionedTaskProbe));

        Assert.False(settings.RequiresExpectedVersion);
        Assert.Equal(typeof(VersionedProbe), settings.AggregateRoot);
    }

    [Fact]
    public void WellShapedTasks_Pass() =>
        Assert.Null(Record.Exception(() => Check(typeof(EntityTaskProbe), typeof(CollectionTaskProbe), typeof(VersionedTaskProbe))));

    [Fact]
    public void Guard_Refuses_TaskWithSecondEffect() => AssertRefused(typeof(TaskWithSecondEffectProbe), "implements more than one effect");

    [Fact]
    public void Guard_Refuses_BareMarker() => AssertRefused(typeof(BareTaskMarkerProbe), "implements ITaskCommand alone");

    // The general ISelectable rule is the refusal: a collection target, task or not, has no row to select.
    [Fact]
    public void Guard_Refuses_SelectableCollectionTask() =>
        AssertRefused(typeof(SelectableCollectionTaskProbe), "is ISelectable but its target is Collection");

    [Fact]
    public void Guard_Refuses_ChildTargetTask() => AssertRefused(typeof(ChildTargetTaskProbe), ChildTaskRefusal);

    [Fact]
    public void Guard_Refuses_RootTargetTask() => AssertRefused(typeof(RootTargetTaskProbe), ChildTaskRefusal);

    private const string ChildTaskRefusal = "is a task on a child entity or a root's child collection";

    /// <summary>Refused naming the type, with the given rule's reason right after the name.</summary>
    private static void AssertRefused(Type requestType, string reason)
    {
        InvalidOperationException refused = Assert.Throws<InvalidOperationException>(() => Check(requestType));

        Assert.Contains($"{requestType.FullName} {reason}", refused.Message, StringComparison.Ordinal);
    }

    private static void Check(params Type[] requests) =>
        CommandTypeGuard.Check(
            requests,
            CommandConfigurationRegistry.Empty,
            AggregateFeatureRegistry.FromConfigurationTypes([]),
            requireTyped: true);
}
