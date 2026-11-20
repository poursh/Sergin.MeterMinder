using Sergin.MeterMinder.IntegrationTests.All.Tasks;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Types;

namespace Sergin.MeterMinder.IntegrationTests.All.Commands.Types;

// ---- Well-shaped tasks ----

internal sealed record EntityTaskProbe(ScheduleProbeId Id) : ITaskCommand<ScheduleProbe, ScheduleProbeId>;

internal sealed record CollectionTaskProbe : ITaskCommand<ScheduleProbe>;

internal sealed record VersionedTaskProbe(VersionedProbeId Id) : ITaskCommand<VersionedProbe, VersionedProbeId>;

// ---- Misshapen tasks: each refused by the type guard ----

internal sealed record TaskWithSecondEffectProbe(ScheduleProbeId Id)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>, IUpdateCommand<ScheduleProbe, ScheduleProbeId>;

/// <summary>Implements only the non-generic marker, which is for guards and constraints.</summary>
internal sealed record BareTaskMarkerProbe : ICommand, ITaskCommand;

/// <summary>A collection task has no record to select.</summary>
internal sealed record SelectableCollectionTaskProbe : ITaskCommand<ScheduleProbe>, ISelectable;

/// <summary>Child tasks are dropped: a task targets a root entity or the root's collection.</summary>
internal sealed record ChildTargetTaskProbe(ScheduleProbeId RootId, ScheduleProbeId Id, ScheduleProbeChildId ChildId)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>,
        IChildEntityTarget<ScheduleProbe, ScheduleProbeId, ScheduleProbeChild, ScheduleProbeChildId>
{
    ScheduleProbeChildId IChildEntityTarget<ScheduleProbe, ScheduleProbeId, ScheduleProbeChild, ScheduleProbeChildId>.Id => ChildId;
}

/// <summary>Nor a task on a root's child collection.</summary>
internal sealed record RootTargetTaskProbe(ScheduleProbeId Id, ScheduleProbeId RootId)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>, IRootTarget<ScheduleProbe, ScheduleProbeId>;
