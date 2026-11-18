using ErrorOr;
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

// ---- Background probes: valid declarations, in ScheduledTaskTestHost.ProbeConfigurations; handlers registered per test host. ----

/// <summary>A background command whose handler waits on the host's <see cref="ProbeGate"/> until a test releases it.</summary>
internal sealed record SlowProbeCommand(ScheduleProbeId Id, string Note)
    : ICommand<TouchProbeResponse>, ITransitionCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

/// <summary>A background command with a one-second timeout whose handler takes thirty.</summary>
internal sealed record TimedProbeCommand(ScheduleProbeId Id)
    : ICommand<TouchProbeResponse>, ITransitionCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

internal sealed class SlowProbeCommandConfiguration : ICommandConfiguration<SlowProbeCommand>
{
    // The name is shared with BackgroundProbeCommandConfiguration (Task 1's fixture), which lives in a separate source.
    public void Configure(CommandConfigurationBuilder<SlowProbeCommand> builder) =>
        builder.Schedulable("tst.probes.slow").Background().RequirePermissions(ScheduleProbePermissions.Touch);
}

internal sealed class TimedProbeCommandConfiguration : ICommandConfiguration<TimedProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<TimedProbeCommand> builder) =>
        builder.Schedulable("tst.probes.timed").Background(TimeSpan.FromSeconds(1)).RequirePermissions(ScheduleProbePermissions.Touch);
}

/// <summary>A background command with a timeout far longer than any test, whose handler waits on the <see cref="ProbeGate"/>.</summary>
internal sealed record GatedTimedProbeCommand(ScheduleProbeId Id)
    : ICommand<TouchProbeResponse>, ITransitionCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

/// <summary>A background command with a one-second timeout whose handler turns the cancellation into another exception.</summary>
internal sealed record WrappingTimedProbeCommand(ScheduleProbeId Id)
    : ICommand<TouchProbeResponse>, ITransitionCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

internal sealed class GatedTimedProbeCommandConfiguration : ICommandConfiguration<GatedTimedProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<GatedTimedProbeCommand> builder) =>
        builder.Schedulable("tst.probes.gated-timed").Background(TimeSpan.FromMinutes(1)).RequirePermissions(ScheduleProbePermissions.Touch);
}

internal sealed class WrappingTimedProbeCommandConfiguration : ICommandConfiguration<WrappingTimedProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<WrappingTimedProbeCommand> builder) =>
        builder.Schedulable("tst.probes.wrapping-timed").Background(TimeSpan.FromSeconds(1)).RequirePermissions(ScheduleProbePermissions.Touch);
}

internal sealed class SlowProbeCommandValidator : AbstractValidator<SlowProbeCommand>
{
    public SlowProbeCommandValidator() => RuleFor(command => command.Note).NotEqual("invalid");
}

/// <summary>Holds every <see cref="SlowProbeCommand"/> send until <see cref="Release"/>; counts the sends that entered.</summary>
internal sealed class ProbeGate
{
    private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int entered;

    public int Entered => Volatile.Read(ref entered);

    public Task WaitAsync(CancellationToken ct)
    {
        Interlocked.Increment(ref entered);
        return released.Task.WaitAsync(ct);
    }

    public void Release() => released.TrySetResult();
}

internal sealed class SlowProbeCommandHandler(ProbeGate gate) : ICommandHandler<SlowProbeCommand, TouchProbeResponse>
{
    public async Task<ErrorOr<TouchProbeResponse>> Handle(SlowProbeCommand request, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        return new TouchProbeResponse(request.Note);
    }
}

internal sealed class GatedTimedProbeCommandHandler(ProbeGate gate) : ICommandHandler<GatedTimedProbeCommand, TouchProbeResponse>
{
    public async Task<ErrorOr<TouchProbeResponse>> Handle(GatedTimedProbeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await gate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException exception) when (cancellationToken.IsCancellationRequested)
        {
            // Distinctive, so a test can tell the send's own cancellation from one a later save throws.
            throw new ProbeCanceledException("The gated probe was cancelled.", exception);
        }

        return new TouchProbeResponse(string.Empty);
    }
}

/// <summary>The cancellation <see cref="GatedTimedProbeCommandHandler"/> surfaces, told apart from any other.</summary>
public sealed class ProbeCanceledException : OperationCanceledException
{
    public ProbeCanceledException()
    {
    }

    public ProbeCanceledException(string message)
        : base(message)
    {
    }

    public ProbeCanceledException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal sealed class WrappingTimedProbeCommandHandler : ICommandHandler<WrappingTimedProbeCommand, TouchProbeResponse>
{
    public async Task<ErrorOr<TouchProbeResponse>> Handle(WrappingTimedProbeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            throw new InvalidOperationException("The probe wrapped its cancellation.", exception);
        }

        return new TouchProbeResponse(string.Empty);
    }
}

internal sealed class TimedProbeCommandHandler : ICommandHandler<TimedProbeCommand, TouchProbeResponse>
{
    public async Task<ErrorOr<TouchProbeResponse>> Handle(TimedProbeCommand request, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        return new TouchProbeResponse(string.Empty);
    }
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

// ---- Argument-template fixtures: built directly, never configured or registered. ----

/// <summary>A value object whose constructor refuses one value, as a domain value object refuses a bad one.</summary>
internal sealed record CheckedNote
{
    public CheckedNote(string value)
    {
        if (value == "refused")
        {
            throw new ArgumentException("The note was refused.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }
}

internal sealed record CheckedProbeCommand(ScheduleProbeId Id, CheckedNote Note) : ICommand<TouchProbeResponse>;

// ---- Background declaration fixtures: never added to ScheduledTaskTestHost.ProbeConfigurations. ----

internal sealed record BackgroundProbeCommand(ScheduleProbeId Id) : ICommand, ISetPropertyCommand<ScheduleProbe, ScheduleProbeId>;

internal sealed class BackgroundProbeCommandConfiguration : ICommandConfiguration<BackgroundProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<BackgroundProbeCommand> builder) =>
        builder.Schedulable("tst.probes.slow").Background(TimeSpan.FromMinutes(2));
}

internal sealed class BackgroundOnlyProbeCommandConfiguration : ICommandConfiguration<BackgroundProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<BackgroundProbeCommand> builder) => builder.Background();
}

internal sealed class BackgroundZeroProbeCommandConfiguration : ICommandConfiguration<BackgroundProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<BackgroundProbeCommand> builder) =>
        builder.Schedulable("tst.probes.slow").Background(TimeSpan.Zero);
}

internal sealed class BackgroundNegativeProbeCommandConfiguration : ICommandConfiguration<BackgroundProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<BackgroundProbeCommand> builder) =>
        builder.Schedulable("tst.probes.slow").Background(TimeSpan.FromSeconds(-1));
}

internal sealed class BackgroundTooLongProbeCommandConfiguration : ICommandConfiguration<BackgroundProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<BackgroundProbeCommand> builder) =>
        builder.Schedulable("tst.probes.slow").Background(TimeSpan.FromMinutes(61));
}
