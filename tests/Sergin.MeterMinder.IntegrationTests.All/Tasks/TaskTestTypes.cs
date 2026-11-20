using ErrorOr;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Tasks;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.Securities;

namespace Sergin.MeterMinder.IntegrationTests.All.Tasks;

// ---- Reusable types: later scheduled-task tasks register these in their hosts. ----

internal sealed record ScheduleProbeId(Guid Value);

internal sealed record ScheduleProbeChildId(Guid Value);

internal sealed record ProbeNote(string Value);

internal sealed class ScheduleProbeChild : Entity<ScheduleProbeChildId>;

internal sealed class ScheduleProbe : AggregateRoot<ScheduleProbeId>
{
    // The navigation is what makes the child reachable from its root in the EF model (TasksDbContext).
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

internal sealed record TouchProbeCommand(ScheduleProbeId Id, ProbeNote Note)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

internal sealed record TouchOneProbeCommand(ScheduleProbeId Id, ProbeNote Note) : ITaskCommand<ScheduleProbe, ScheduleProbeId>;

/// <summary>Typed but never configured, so never schedulable.</summary>
internal sealed record UnconfiguredProbeCommand(ScheduleProbeId Id, ProbeNote Note) : ITaskCommand<ScheduleProbe, ScheduleProbeId>;

internal sealed class TouchProbeCommandConfiguration : ICommandConfiguration<TouchProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<TouchProbeCommand> builder) =>
        builder.Schedulable("tst.probes.touch").RequirePermissions(ScheduleProbePermissions.Touch);
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

// ---- Background probes: valid declarations, in TaskTestHost.ProbeConfigurations; handlers registered per test host. ----

/// <summary>A background command whose handler waits on the host's <see cref="ProbeGate"/> until a test releases it.</summary>
internal sealed record SlowProbeCommand(ScheduleProbeId Id, string Note)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

/// <summary>A background command with a one-second timeout whose handler takes thirty.</summary>
internal sealed record TimedProbeCommand(ScheduleProbeId Id)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

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

/// <summary>A background command with a three-second timeout whose handler takes thirty: a cancel well before it wins.</summary>
internal sealed record LongTimedProbeCommand(ScheduleProbeId Id)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

internal sealed class LongTimedProbeCommandConfiguration : ICommandConfiguration<LongTimedProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<LongTimedProbeCommand> builder) =>
        builder.Schedulable("tst.probes.long-timed").Background(TimeSpan.FromSeconds(3)).RequirePermissions(ScheduleProbePermissions.Touch);
}

/// <summary>A background command with a timeout far longer than any test, whose handler waits on the <see cref="ProbeGate"/>.</summary>
internal sealed record GatedTimedProbeCommand(ScheduleProbeId Id)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

/// <summary>A background command with a one-second timeout whose handler turns the cancellation into another exception.</summary>
internal sealed record WrappingTimedProbeCommand(ScheduleProbeId Id)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

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

internal sealed class SlowProbeCommandHandler(ProbeGate gate) : ICommandHandler<SlowProbeCommand>
{
    public async Task<ErrorOr<Success>> Handle(SlowProbeCommand request, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        return Result.Success;
    }
}

internal sealed class GatedTimedProbeCommandHandler(ProbeGate gate) : ICommandHandler<GatedTimedProbeCommand>
{
    public async Task<ErrorOr<Success>> Handle(GatedTimedProbeCommand request, CancellationToken cancellationToken)
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

        return Result.Success;
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

internal sealed class WrappingTimedProbeCommandHandler : ICommandHandler<WrappingTimedProbeCommand>
{
    public async Task<ErrorOr<Success>> Handle(WrappingTimedProbeCommand request, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        }
        catch (OperationCanceledException exception)
        {
            throw new InvalidOperationException("The probe wrapped its cancellation.", exception);
        }

        return Result.Success;
    }
}

internal sealed class TimedProbeCommandHandler : ICommandHandler<TimedProbeCommand>
{
    public async Task<ErrorOr<Success>> Handle(TimedProbeCommand request, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        return Result.Success;
    }
}

internal sealed class LongTimedProbeCommandHandler : ICommandHandler<LongTimedProbeCommand>
{
    public async Task<ErrorOr<Success>> Handle(LongTimedProbeCommand request, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
        return Result.Success;
    }
}

/// <summary>
/// A background command whose handler cancels its own run (through <see cref="ProbeRunCanceller"/>) and then throws: a
/// retryable failure of an item whose run was cancelled while it ran.
/// </summary>
internal sealed record CancelThenThrowProbeCommand(ScheduleProbeId Id)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

internal sealed class CancelThenThrowProbeCommandConfiguration : ICommandConfiguration<CancelThenThrowProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<CancelThenThrowProbeCommand> builder) =>
        builder.Schedulable("tst.probes.cancel-then-throw").Background().RequirePermissions(ScheduleProbePermissions.Touch);
}

/// <summary>The run a <see cref="CancelThenThrowProbeCommandHandler"/> cancels; set by the test before the drain.</summary>
internal sealed class ProbeRunCanceller
{
    public Guid RunId { get; set; }
}

internal sealed class CancelThenThrowProbeCommandHandler(IServiceScopeFactory scopes, ProbeRunCanceller canceller)
    : ICommandHandler<CancelThenThrowProbeCommand>
{
    public async Task<ErrorOr<Success>> Handle(CancelThenThrowProbeCommand request, CancellationToken cancellationToken)
    {
        // Another scope, as another process's cancel would be: committed before the send fails.
        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<ITaskStoreResolver>().For(TasksDbContext.Schema)
                .CancelRunAsync(canceller.RunId, DateTime.UtcNow, cancellationToken);
        }

        throw new InvalidOperationException("The probe failed after its run was cancelled.");
    }
}

/// <summary>A background command whose handler reports progress through <see cref="ITaskProgress"/>, as <see cref="ProgressProbe"/> scripts it.</summary>
internal sealed record ProgressProbeCommand(ScheduleProbeId Id)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

internal sealed class ProgressProbeCommandConfiguration : ICommandConfiguration<ProgressProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<ProgressProbeCommand> builder) =>
        builder.Schedulable("tst.probes.progress").Background().RequirePermissions(ScheduleProbePermissions.Touch);
}

/// <summary>
/// The script of every <see cref="ProgressProbeCommand"/> send: wait for <see cref="Start"/>, report each of
/// <see cref="Reports"/>, read the item's stored progress back (another scope, as the runs dialog would), then wait for
/// <see cref="Finish"/>. Both gates are open unless a test closes them.
/// </summary>
internal sealed class ProgressProbe
{
    private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int entered;

    public IReadOnlyList<int> Reports { get; set; } = [];

    /// <summary>Called after each report; a test moves its clock here.</summary>
    public Action AfterEachReport { get; set; } = () => { };

    public ProbeGate Start { get; } = new();

    public ProbeGate Finish { get; } = new();

    /// <summary>The item's stored progress, read once every report was made and before the send returned.</summary>
    public int? StoredAfterReports { get; set; }

    public int Entered => Volatile.Read(ref entered);

    public Task Started => started.Task;

    public void Enter()
    {
        Interlocked.Increment(ref entered);
        started.TrySetResult();
    }

    public void OpenGates()
    {
        Start.Release();
        Finish.Release();
    }
}

internal sealed class ProgressProbeCommandHandler(ProgressProbe probe, ITaskProgress progress, IRunItemContext runItem, IServiceScopeFactory scopes)
    : ICommandHandler<ProgressProbeCommand>
{
    public async Task<ErrorOr<Success>> Handle(ProgressProbeCommand request, CancellationToken cancellationToken)
    {
        probe.Enter();
        await probe.Start.WaitAsync(cancellationToken);

        foreach (int report in probe.Reports)
        {
            await progress.ReportAsync(report, cancellationToken);
            probe.AfterEachReport();
        }

        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            probe.StoredAfterReports = await scope.ServiceProvider.GetRequiredService<TasksDbContext>().TaskRunItems
                .AsNoTracking()
                .Where(item => item.Id == runItem.RunItemId)
                .Select(item => item.Progress)
                .SingleAsync(cancellationToken);
        }

        await probe.Finish.WaitAsync(cancellationToken);
        return Result.Success;
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

internal sealed record MalformedNameCommand(ScheduleProbeId Id) : ITaskCommand<ScheduleProbe, ScheduleProbeId>;

internal sealed class MalformedNameCommandConfiguration : ICommandConfiguration<MalformedNameCommand>
{
    public void Configure(CommandConfigurationBuilder<MalformedNameCommand> builder) => builder.Schedulable("Tst.Bad");
}

internal sealed record DuplicateNameOneCommand(ScheduleProbeId Id) : ITaskCommand<ScheduleProbe, ScheduleProbeId>;

internal sealed record DuplicateNameTwoCommand(ScheduleProbeId Id) : ITaskCommand<ScheduleProbe, ScheduleProbeId>;

internal sealed class DuplicateNameOneCommandConfiguration : ICommandConfiguration<DuplicateNameOneCommand>
{
    public void Configure(CommandConfigurationBuilder<DuplicateNameOneCommand> builder) => builder.Schedulable("tst.probes.dup");
}

internal sealed class DuplicateNameTwoCommandConfiguration : ICommandConfiguration<DuplicateNameTwoCommand>
{
    public void Configure(CommandConfigurationBuilder<DuplicateNameTwoCommand> builder) => builder.Schedulable("tst.probes.dup");
}

internal sealed record OutsideSchemaCommand(ScheduleProbeId Id) : ITaskCommand<ScheduleProbe, ScheduleProbeId>;

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

internal sealed record CheckedProbeCommand(ScheduleProbeId Id, CheckedNote Note) : ICommand;

// ---- Background declaration fixtures: never added to TaskTestHost.ProbeConfigurations. ----

internal sealed record BackgroundProbeCommand(ScheduleProbeId Id) : ITaskCommand<ScheduleProbe, ScheduleProbeId>;

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

/// <summary>A set-property command: the generated views send it with a value, so it may not be a background command.</summary>
internal sealed record BackgroundSetPropertyProbeCommand(ScheduleProbeId Id, ProbeNote Note)
    : ICommand, ISetPropertyCommand<ScheduleProbe, ScheduleProbeId>;

internal sealed class BackgroundSetPropertyProbeCommandConfiguration : ICommandConfiguration<BackgroundSetPropertyProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<BackgroundSetPropertyProbeCommand> builder) =>
        builder.Schedulable("tst.probes.background-set").Background();
}

/// <summary>A child's transition: the generated views start only a root's.</summary>
internal sealed record BackgroundChildTransitionProbeCommand(ScheduleProbeId RootId, ScheduleProbeChildId Id)
    : ICommand, ITransitionCommand<ScheduleProbe, ScheduleProbeId, ScheduleProbeChild, ScheduleProbeChildId>;

internal sealed class BackgroundChildTransitionProbeCommandConfiguration : ICommandConfiguration<BackgroundChildTransitionProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<BackgroundChildTransitionProbeCommand> builder) =>
        builder.Schedulable("tst.probes.background-child").Background();
}

/// <summary>A deletion: the generated views confirm and send it, so it may not be a background command.</summary>
internal sealed record BackgroundDeleteProbeCommand(ScheduleProbeId Id) : ICommand, IDeleteCommand<ScheduleProbe, ScheduleProbeId>;

internal sealed class BackgroundDeleteProbeCommandConfiguration : ICommandConfiguration<BackgroundDeleteProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<BackgroundDeleteProbeCommand> builder) =>
        builder.Schedulable("tst.probes.background-delete").Background();
}

/// <summary>Throws a cancellation no token caused, as an HTTP client's timeout does.</summary>
internal sealed class SelfCancelingProbeCommandHandler : ICommandHandler<TouchOneProbeCommand>
{
    public Task<ErrorOr<Success>> Handle(TouchOneProbeCommand request, CancellationToken cancellationToken) =>
        throw new TaskCanceledException("The probe's own request timed out.");
}
