using FluentValidation;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.Securities;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

// ---- Reusable types: later scheduled-task tasks register these in their hosts. ----

internal sealed record ScheduleProbeId(Guid Value);

internal sealed record ScheduleProbeChildId(Guid Value);

internal sealed record ProbeNote(string Value);

internal sealed class ScheduleProbeChild : Entity<ScheduleProbeChildId>;

internal sealed class ScheduleProbe : AggregateRoot<ScheduleProbeId>
{
    // The navigation is what makes the child reachable from its root in the EF model (ScheduledTasksDbContext).
    private readonly List<ScheduleProbeChild> children = [];

    private ScheduleProbe()
    {
    }

    public IReadOnlyCollection<ScheduleProbeChild> Children => children;

    /// <summary>The note the last touch left; what a scheduled send changes.</summary>
    public string? LastNote { get; private set; }

    public static ScheduleProbe Create() => new() { Id = new ScheduleProbeId(Guid.CreateVersion7()) };

    public void Touch(ProbeNote note) => LastNote = note.Value;
}

internal static class ScheduleProbePermissions
{
    public static PermissionDefinition Read { get; } =
        PermissionDefinition.Create("permission.tst.probes.read", "View probes");

    public static PermissionDefinition Touch { get; } =
        PermissionDefinition.Create("permission.tst.probes.touch", "Touch probes");
}

internal sealed class ScheduleProbeAggregateFeatureConfiguration : IAggregateFeatureConfiguration<ScheduleProbe>
{
    public void Configure(AggregateFeatureBuilder<ScheduleProbe> builder) =>
        builder.Audited().Scoped().Permissions(ScheduleProbePermissions.Read, ScheduleProbePermissions.Touch);
}

internal sealed record TouchProbeResponse(string Note);

internal sealed record TouchProbeCommand(ScheduleProbeId Id, ProbeNote Note)
    : ICommand<TouchProbeResponse>, ISetPropertyCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

internal sealed record TouchProbeChildCommand(ScheduleProbeId RootId, ScheduleProbeChildId Id, ProbeNote Note)
    : ICommand<TouchProbeResponse>,
        ISetPropertyCommand<ScheduleProbe, ScheduleProbeId, ScheduleProbeChild, ScheduleProbeChildId>,
        ISelectable;

internal sealed record TouchOneProbeCommand(ScheduleProbeId Id, ProbeNote Note)
    : ICommand<TouchProbeResponse>, ISetPropertyCommand<ScheduleProbe, ScheduleProbeId>;

/// <summary>Typed but never configured, so never schedulable.</summary>
internal sealed record UnconfiguredProbeCommand(ScheduleProbeId Id, ProbeNote Note)
    : ICommand<TouchProbeResponse>, ISetPropertyCommand<ScheduleProbe, ScheduleProbeId>;

internal sealed class TouchProbeCommandConfiguration : ICommandConfiguration<TouchProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<TouchProbeCommand> builder) =>
        builder.Schedulable("tst.probes.touch").RequirePermissions(ScheduleProbePermissions.Touch);
}

internal sealed class TouchProbeChildCommandConfiguration : ICommandConfiguration<TouchProbeChildCommand>
{
    public void Configure(CommandConfigurationBuilder<TouchProbeChildCommand> builder) =>
        builder.Schedulable("tst.probes.touch-child").RequirePermissions(ScheduleProbePermissions.Touch);
}

internal sealed class TouchOneProbeCommandConfiguration : ICommandConfiguration<TouchOneProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<TouchOneProbeCommand> builder) =>
        builder.Schedulable("tst.probes.touch-one").RequirePermissions(ScheduleProbePermissions.Touch);
}

internal sealed class TouchProbeCommandValidator : AbstractValidator<TouchProbeCommand>
{
    public TouchProbeCommandValidator() =>
        RuleFor(command => command.Note.Value)
            .NotEqual("invalid")
            .OverridePropertyName(nameof(TouchProbeCommand.Note));
}

// ---- Refusal fixtures: each configuration is fed only to the test that needs it. ----

internal sealed record VersionedProbeId(Guid Value);

internal sealed class VersionedProbe : AggregateRoot<VersionedProbeId>;

internal sealed class VersionedProbeAggregateFeatureConfiguration : IAggregateFeatureConfiguration<VersionedProbe>
{
    public void Configure(AggregateFeatureBuilder<VersionedProbe> builder) => builder.Versioned();
}

internal sealed record GuardedProbeCommand(VersionedProbeId Id) : ICommand, IUpdateCommand<VersionedProbe, VersionedProbeId>;

internal sealed class GuardedProbeCommandConfiguration : ICommandConfiguration<GuardedProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<GuardedProbeCommand> builder) => builder.Schedulable("tst.guarded.touch");
}

internal sealed class WaivedGuardedProbeCommandConfiguration : ICommandConfiguration<GuardedProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<GuardedProbeCommand> builder) =>
        builder.Schedulable("tst.guarded.touch").WithoutExpectedVersion();
}

internal sealed record CreateProbeCommand(string Name) : ICommand, ICreateCommand<ScheduleProbe, ScheduleProbeId>;

internal sealed class CreateProbeCommandConfiguration : ICommandConfiguration<CreateProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<CreateProbeCommand> builder) => builder.Schedulable("tst.probes.create");
}

internal sealed record SystemProbeCommand : ICommand, ISystemCommand;

internal sealed class SystemProbeCommandConfiguration : ICommandConfiguration<SystemProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<SystemProbeCommand> builder) => builder.Schedulable("tst.probes.system");
}

internal sealed record UntypedProbeCommand : ICommand;

internal sealed class UntypedProbeCommandConfiguration : ICommandConfiguration<UntypedProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<UntypedProbeCommand> builder) => builder.Schedulable("tst.probes.untyped");
}

internal sealed record MalformedNameCommand(ScheduleProbeId Id) : ICommand, ISetPropertyCommand<ScheduleProbe, ScheduleProbeId>;

internal sealed class MalformedNameCommandConfiguration : ICommandConfiguration<MalformedNameCommand>
{
    public void Configure(CommandConfigurationBuilder<MalformedNameCommand> builder) => builder.Schedulable("Tst.Bad");
}

internal sealed record DuplicateNameOneCommand(ScheduleProbeId Id) : ICommand, ISetPropertyCommand<ScheduleProbe, ScheduleProbeId>;

internal sealed record DuplicateNameTwoCommand(ScheduleProbeId Id) : ICommand, ISetPropertyCommand<ScheduleProbe, ScheduleProbeId>;

internal sealed class DuplicateNameOneCommandConfiguration : ICommandConfiguration<DuplicateNameOneCommand>
{
    public void Configure(CommandConfigurationBuilder<DuplicateNameOneCommand> builder) => builder.Schedulable("tst.probes.dup");
}

internal sealed class DuplicateNameTwoCommandConfiguration : ICommandConfiguration<DuplicateNameTwoCommand>
{
    public void Configure(CommandConfigurationBuilder<DuplicateNameTwoCommand> builder) => builder.Schedulable("tst.probes.dup");
}

internal sealed record OutsideSchemaCommand(ScheduleProbeId Id) : ICommand, ISetPropertyCommand<ScheduleProbe, ScheduleProbeId>;

internal sealed class OutsideSchemaCommandConfiguration : ICommandConfiguration<OutsideSchemaCommand>
{
    public void Configure(CommandConfigurationBuilder<OutsideSchemaCommand> builder) => builder.Schedulable("dm.x");
}
