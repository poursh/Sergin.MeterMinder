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

/// <summary>A task never configured: every default, its name derived, and not schedulable.</summary>
internal sealed record UnconfiguredProbeCommand(ScheduleProbeId Id, ProbeNote Note) : ITaskCommand<ScheduleProbe, ScheduleProbeId>;

internal sealed class TouchProbeCommandConfiguration : ICommandConfiguration<TouchProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<TouchProbeCommand> builder) =>
        builder.RequirePermissions(ScheduleProbePermissions.Touch).Task(task => task.Schedulable().Name("tst.probes.touch"));
}

internal sealed class TouchOneProbeCommandConfiguration : ICommandConfiguration<TouchOneProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<TouchOneProbeCommand> builder) =>
        builder.RequirePermissions(ScheduleProbePermissions.Touch).Task(task => task.Schedulable().Name("tst.probes.touch-one"));
}

/// <summary>A configured task that is not <c>Schedulable()</c>: it can be started now, never saved on a schedule or run once.</summary>
internal sealed record StartOnlyProbeCommand(ScheduleProbeId Id, ProbeNote Note) : ITaskCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

internal sealed class StartOnlyProbeCommandConfiguration : ICommandConfiguration<StartOnlyProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<StartOnlyProbeCommand> builder) =>
        builder.RequirePermissions(ScheduleProbePermissions.Touch).Task(task => task.Name("tst.probes.start-only"));
}

internal sealed class TouchProbeCommandValidator : AbstractValidator<TouchProbeCommand>
{
    public TouchProbeCommandValidator() =>
        RuleFor(command => command.Note.Value)
            .NotEqual("invalid")
            .OverridePropertyName(nameof(TouchProbeCommand.Note));
}

/// <summary>A non-task on the probe, unconfigured: the guard lets it through inline, as it does every command that is not a task.</summary>
internal sealed record NoteProbeCommand(ScheduleProbeId Id, ProbeNote Note) : ICommand, IUpdateCommand<ScheduleProbe, ScheduleProbeId>;

internal sealed class NoteProbeCommandHandler : ICommandHandler<NoteProbeCommand>
{
    public Task<ErrorOr<Success>> Handle(NoteProbeCommand request, CancellationToken cancellationToken) =>
        Task.FromResult<ErrorOr<Success>>(Result.Success);
}

// ---- Run probes: valid declarations, in TaskTestHost.ProbeConfigurations; handlers registered per test host. ----

/// <summary>A background command whose handler waits on the host's <see cref="ProbeGate"/> until a test releases it.</summary>
internal sealed record SlowProbeCommand(ScheduleProbeId Id, string Note)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

/// <summary>A task with a one-second inactivity timeout whose handler runs <see cref="PacedProbe.Script"/>.</summary>
internal sealed record PacedProbeCommand(ScheduleProbeId Id)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

/// <summary>A task with a one-second inactivity timeout and a two-second cap whose handler runs <see cref="PacedProbe.Script"/>.</summary>
internal sealed record CappedProbeCommand(ScheduleProbeId Id)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

internal sealed class SlowProbeCommandConfiguration : ICommandConfiguration<SlowProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<SlowProbeCommand> builder) =>
        builder.RequirePermissions(ScheduleProbePermissions.Touch).Task(task => task.Schedulable().Name("tst.probes.slow"));
}

internal sealed class PacedProbeCommandConfiguration : ICommandConfiguration<PacedProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<PacedProbeCommand> builder) =>
        builder.RequirePermissions(ScheduleProbePermissions.Touch).Task(task => task.InactivityTimeout(TimeSpan.FromSeconds(1)).Name("tst.probes.paced"));
}

internal sealed class CappedProbeCommandConfiguration : ICommandConfiguration<CappedProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<CappedProbeCommand> builder) =>
        builder.RequirePermissions(ScheduleProbePermissions.Touch)
            .Task(task => task.InactivityTimeout(TimeSpan.FromSeconds(1)).MaxDuration(TimeSpan.FromSeconds(2)).Name("tst.probes.capped"));
}

/// <summary>A task with a three-second inactivity timeout whose handler is silent for thirty: a cancel well before it wins.</summary>
internal sealed record LongTimedProbeCommand(ScheduleProbeId Id)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

internal sealed class LongTimedProbeCommandConfiguration : ICommandConfiguration<LongTimedProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<LongTimedProbeCommand> builder) =>
        builder.RequirePermissions(ScheduleProbePermissions.Touch).Task(task => task.InactivityTimeout(TimeSpan.FromSeconds(3)).Schedulable().Name("tst.probes.long-timed"));
}

/// <summary>A background command with a timeout far longer than any test, whose handler waits on the <see cref="ProbeGate"/>.</summary>
internal sealed record GatedTimedProbeCommand(ScheduleProbeId Id)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

/// <summary>A task with a one-second inactivity timeout, silent, whose handler turns the cancellation into another exception.</summary>
internal sealed record WrappingTimedProbeCommand(ScheduleProbeId Id)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

internal sealed class GatedTimedProbeCommandConfiguration : ICommandConfiguration<GatedTimedProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<GatedTimedProbeCommand> builder) =>
        builder.RequirePermissions(ScheduleProbePermissions.Touch).Task(task => task.InactivityTimeout(TimeSpan.FromMinutes(1)).Schedulable().Name("tst.probes.gated-timed"));
}

internal sealed class WrappingTimedProbeCommandConfiguration : ICommandConfiguration<WrappingTimedProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<WrappingTimedProbeCommand> builder) =>
        builder.RequirePermissions(ScheduleProbePermissions.Touch).Task(task => task.InactivityTimeout(TimeSpan.FromSeconds(1)).Schedulable().Name("tst.probes.wrapping-timed"));
}

/// <summary>A background, schedulable task that declares <c>NotCancelable()</c>: its run cannot be cancelled by anyone.</summary>
internal sealed record FixedProbeCommand(ScheduleProbeId Id, string Note)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

internal sealed class FixedProbeCommandConfiguration : ICommandConfiguration<FixedProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<FixedProbeCommand> builder) =>
        builder.RequirePermissions(ScheduleProbePermissions.Touch).Task(task => task.NotCancelable().Schedulable().Name("tst.probes.fixed"));
}

internal sealed class FixedProbeCommandHandler(ProbeGate gate) : ICommandHandler<FixedProbeCommand>
{
    public async Task<ErrorOr<Success>> Handle(FixedProbeCommand request, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        return Result.Success;
    }
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

/// <summary>
/// What every <see cref="PacedProbeCommand"/> and <see cref="CappedProbeCommand"/> send does: the test sets
/// <see cref="Script"/>, which gets the send's <see cref="ITaskProgress"/> and token.
/// </summary>
internal sealed class PacedProbe
{
    public Func<ITaskProgress, CancellationToken, Task> Script { get; set; } = (_, _) => Task.CompletedTask;

    /// <summary>
    /// A script that calls <paramref name="beat"/> every <paramref name="every"/> for <paramref name="total"/>, waiting on
    /// the send's token.
    /// </summary>
    public static Func<ITaskProgress, CancellationToken, Task> Every(
        TimeSpan every, TimeSpan total, Func<ITaskProgress, int, CancellationToken, ValueTask> beat) =>
        async (progress, ct) =>
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            int step = 0;

            while (watch.Elapsed < total)
            {
                await Task.Delay(every, ct);
                step++;
                await beat(progress, step, ct);
            }
        };
}

internal sealed class PacedProbeCommandHandler(PacedProbe probe, ITaskProgress progress) : ICommandHandler<PacedProbeCommand>
{
    public async Task<ErrorOr<Success>> Handle(PacedProbeCommand request, CancellationToken cancellationToken)
    {
        await probe.Script(progress, cancellationToken);
        return Result.Success;
    }
}

internal sealed class CappedProbeCommandHandler(PacedProbe probe, ITaskProgress progress) : ICommandHandler<CappedProbeCommand>
{
    public async Task<ErrorOr<Success>> Handle(CappedProbeCommand request, CancellationToken cancellationToken)
    {
        await probe.Script(progress, cancellationToken);
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
        builder.RequirePermissions(ScheduleProbePermissions.Touch).Task(task => task.Schedulable().Name("tst.probes.cancel-then-throw"));
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
        builder.RequirePermissions(ScheduleProbePermissions.Touch).Task(task => task.Schedulable().Name("tst.probes.progress"));
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

/// <summary>A task naming a child entity; the type guard refuses it at host start, the catalogue refuses it on its own.</summary>
internal sealed record ChildTaskProbeCommand(ScheduleProbeId RootId, ScheduleProbeChildId Id)
    : ICommand, ITaskCommand, IChildEntityTarget<ScheduleProbe, ScheduleProbeId, ScheduleProbeChild, ScheduleProbeChildId>;

internal sealed record MalformedNameCommand(ScheduleProbeId Id) : ITaskCommand<ScheduleProbe, ScheduleProbeId>;

internal sealed class MalformedNameCommandConfiguration : ICommandConfiguration<MalformedNameCommand>
{
    public void Configure(CommandConfigurationBuilder<MalformedNameCommand> builder) => builder.Task(task => task.Name("Tst.Bad"));
}

internal sealed record DuplicateNameOneCommand(ScheduleProbeId Id) : ITaskCommand<ScheduleProbe, ScheduleProbeId>;

internal sealed record DuplicateNameTwoCommand(ScheduleProbeId Id) : ITaskCommand<ScheduleProbe, ScheduleProbeId>;

internal sealed class DuplicateNameOneCommandConfiguration : ICommandConfiguration<DuplicateNameOneCommand>
{
    // No Name(...): its derived name is tst.schedule-probe.duplicate-name-one, the one DuplicateNameTwo takes.
    public void Configure(CommandConfigurationBuilder<DuplicateNameOneCommand> builder) => builder.Task(task => task.Schedulable());
}

internal sealed class DuplicateNameTwoCommandConfiguration : ICommandConfiguration<DuplicateNameTwoCommand>
{
    public void Configure(CommandConfigurationBuilder<DuplicateNameTwoCommand> builder) =>
        builder.Task(task => task.Name("tst.schedule-probe.duplicate-name-one"));
}

internal sealed record OutsideSchemaCommand(ScheduleProbeId Id) : ITaskCommand<ScheduleProbe, ScheduleProbeId>;

internal sealed class OutsideSchemaCommandConfiguration : ICommandConfiguration<OutsideSchemaCommand>
{
    public void Configure(CommandConfigurationBuilder<OutsideSchemaCommand> builder) => builder.Task(task => task.Name("dm.x"));
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

// ---- Task declaration fixtures: never added to TaskTestHost.ProbeConfigurations. ----

internal sealed record DeclaredProbeCommand(ScheduleProbeId Id) : ITaskCommand<ScheduleProbe, ScheduleProbeId>;

internal sealed class DeclaredProbeCommandConfiguration : ICommandConfiguration<DeclaredProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<DeclaredProbeCommand> builder) =>
        builder.Task(task => task
            .InactivityTimeout(TimeSpan.FromMinutes(2))
            .MaxDuration(TimeSpan.FromMinutes(10))
            .NotCancelable()
            .Schedulable()
            .Name("tst.probes.old"));
}

internal sealed class ZeroInactivityProbeCommandConfiguration : ICommandConfiguration<DeclaredProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<DeclaredProbeCommand> builder) =>
        builder.Task(task => task.InactivityTimeout(TimeSpan.Zero));
}

internal sealed class LongInactivityProbeCommandConfiguration : ICommandConfiguration<DeclaredProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<DeclaredProbeCommand> builder) =>
        builder.Task(task => task.InactivityTimeout(TimeSpan.FromMinutes(61)));
}

internal sealed class ShortMaxDurationProbeCommandConfiguration : ICommandConfiguration<DeclaredProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<DeclaredProbeCommand> builder) =>
        builder.Task(task => task.InactivityTimeout(TimeSpan.FromMinutes(10)).MaxDuration(TimeSpan.FromMinutes(5)));
}

// ---- Collection probe: a task on every probe of the run's scope, with no record. ----

internal sealed record SyncProbesCommand : ITaskCommand<ScheduleProbe>;

internal sealed class SyncProbesCommandConfiguration : ICommandConfiguration<SyncProbesCommand>
{
    public void Configure(CommandConfigurationBuilder<SyncProbesCommand> builder) =>
        builder.RequirePermissions(ScheduleProbePermissions.Touch).Task(task => task.Schedulable());
}

/// <summary>Succeeds at once; a collection run stays Running only while its item waits on the (fake) engine.</summary>
internal sealed class SyncProbesCommandHandler : ICommandHandler<SyncProbesCommand>
{
    public Task<ErrorOr<Success>> Handle(SyncProbesCommand request, CancellationToken cancellationToken) =>
        Task.FromResult<ErrorOr<Success>>(Result.Success);
}

/// <summary>Throws a cancellation no token caused, as an HTTP client's timeout does.</summary>
internal sealed class SelfCancelingProbeCommandHandler : ICommandHandler<TouchOneProbeCommand>
{
    public Task<ErrorOr<Success>> Handle(TouchOneProbeCommand request, CancellationToken cancellationToken) =>
        throw new TaskCanceledException("The probe's own request timed out.");
}
