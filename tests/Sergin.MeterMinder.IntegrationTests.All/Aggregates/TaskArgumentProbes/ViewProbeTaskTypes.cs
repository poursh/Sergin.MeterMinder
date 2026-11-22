using ErrorOr;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.IntegrationTests.All.Tasks;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.Validations;
using Sergin.SharedKernel.Domain.Repositories;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Repositories;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Configuration;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Model;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates.TaskArgumentProbes;

// The schedule probe (Tasks/TaskTestTypes) as a generated view shows it: a task over one record with arguments (Window:
// a note and a number of hours, validated 1-24, the probe checked to exist within the caller's reach), and one whose only
// argument is NotShown with a default (Kick), so it starts on click. Three view configurations offer Window in each edit
// mode; a test host registers exactly one. Every other probe task the tasks host carries is hidden.

internal sealed record ProbeWindowTaskCommand(ScheduleProbeId Id, ProbeNote Note, int Hours)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

internal sealed class ProbeWindowTaskCommandConfiguration : ICommandConfiguration<ProbeWindowTaskCommand>
{
    public void Configure(CommandConfigurationBuilder<ProbeWindowTaskCommand> builder) =>
        builder.RequirePermissions(ScheduleProbePermissions.Touch).Task(task => task.Name("tst.probes.window"));
}

internal sealed class ProbeWindowTaskCommandValidator : AbstractValidator<ProbeWindowTaskCommand>
{
    public ProbeWindowTaskCommandValidator(IRepository<ScheduleProbe, ScheduleProbeId> probes)
    {
        RuleFor(command => command.Hours).InclusiveBetween(1, 24);
        RuleFor(command => command.Id).MustExistIn(probes, ScheduleProbeTexts.Singular);
    }
}

/// <summary>A task whose one argument is NotShown and has a default: no form field, so it starts on click.</summary>
internal sealed record ProbeKickTaskCommand(ScheduleProbeId Id, int Times = 3)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

internal sealed class ProbeKickTaskCommandConfiguration : ICommandConfiguration<ProbeKickTaskCommand>
{
    public void Configure(CommandConfigurationBuilder<ProbeKickTaskCommand> builder) =>
        builder.RequirePermissions(ScheduleProbePermissions.Touch).NotShown(command => command.Times).Task(task => task.Name("tst.probes.kick"));
}

/// <summary>A schedulable task over one record whose shown argument declares a default: its form starts from it.</summary>
internal sealed record ProbeSweepTaskCommand(ScheduleProbeId Id, ProbeNote Note, int Passes = 2)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

internal sealed class ProbeSweepTaskCommandConfiguration : ICommandConfiguration<ProbeSweepTaskCommand>
{
    public void Configure(CommandConfigurationBuilder<ProbeSweepTaskCommand> builder) =>
        builder.RequirePermissions(ScheduleProbePermissions.Touch).Task(task => task.Schedulable().Name("tst.probes.sweep"));
}

/// <summary>
/// A schedulable task over one record with arguments the Schedule dialog asks for (Hours validated 1-24); its handler
/// keeps what each run item was sent.
/// </summary>
internal sealed record ProbeShiftTaskCommand(ScheduleProbeId Id, ProbeNote Note, int Hours)
    : ITaskCommand<ScheduleProbe, ScheduleProbeId>, ISelectable;

internal sealed class ProbeShiftTaskCommandConfiguration : ICommandConfiguration<ProbeShiftTaskCommand>
{
    public void Configure(CommandConfigurationBuilder<ProbeShiftTaskCommand> builder) =>
        builder.RequirePermissions(ScheduleProbePermissions.Touch).Task(task => task.Schedulable().Name("tst.probes.shift"));
}

internal sealed class ProbeShiftTaskCommandValidator : AbstractValidator<ProbeShiftTaskCommand>
{
    public ProbeShiftTaskCommandValidator() => RuleFor(command => command.Hours).InclusiveBetween(1, 24);
}

/// <summary>Every Shift a run item sent, in order.</summary>
internal sealed class ShiftRecorder
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<ProbeShiftTaskCommand> sent = new();

    public IReadOnlyCollection<ProbeShiftTaskCommand> Sent => [.. sent];

    public void Record(ProbeShiftTaskCommand command) => sent.Enqueue(command);
}

internal sealed class ProbeShiftTaskCommandHandler(ShiftRecorder recorder) : IRequestHandler<ProbeShiftTaskCommand, ErrorOr<Success>>
{
    public Task<ErrorOr<Success>> Handle(ProbeShiftTaskCommand request, CancellationToken cancellationToken)
    {
        recorder.Record(request);
        return Task.FromResult<ErrorOr<Success>>(Result.Success);
    }
}

/// <summary>A schedulable task over the collection with an argument.</summary>
internal sealed record ProbeRefreshTaskCommand(ProbeNote Note) : ITaskCommand<ScheduleProbe>;

internal sealed class ProbeRefreshTaskCommandConfiguration : ICommandConfiguration<ProbeRefreshTaskCommand>
{
    public void Configure(CommandConfigurationBuilder<ProbeRefreshTaskCommand> builder) =>
        builder.RequirePermissions(ScheduleProbePermissions.Touch).Task(task => task.Schedulable().Name("tst.probes.refresh"));
}

/// <summary>A create command with an optional argument: its generated create form starts from the declared default.</summary>
internal sealed record CreateScheduleProbeCommand(ProbeNote Name, int Copies = 2) : ICommand, ICreateCommand<ScheduleProbe, ScheduleProbeId>;

/// <summary>Keeps the last create the form sent; nothing reaches a database.</summary>
internal sealed class CreatedProbeRecorder
{
    public CreateScheduleProbeCommand? Last { get; set; }
}

internal sealed class CreateScheduleProbeCommandHandler(CreatedProbeRecorder recorder) : IRequestHandler<CreateScheduleProbeCommand, ErrorOr<Success>>
{
    public Task<ErrorOr<Success>> Handle(CreateScheduleProbeCommand request, CancellationToken cancellationToken)
    {
        recorder.Last = request;
        return Task.FromResult<ErrorOr<Success>>(Result.Success);
    }
}

internal sealed record ScheduleProbeResponse(Guid Id, string Name);

internal sealed record GetScheduleProbeQuery(ScheduleProbeId Id) : IQuery<ScheduleProbeResponse>, IGetOneQuery<ScheduleProbe, ScheduleProbeId>;

internal sealed class GetScheduleProbeQueryConfiguration : ICommandConfiguration<GetScheduleProbeQuery>
{
    public void Configure(CommandConfigurationBuilder<GetScheduleProbeQuery> builder) => builder.RequirePermissions(ScheduleProbePermissions.Read);
}

internal sealed record ScheduleProbeListItem(Guid Id, string Name);

internal sealed record ListScheduleProbesQuery : ListQuery<ScheduleProbeListItem>, IListOf<ScheduleProbe>
{
    public ListScheduleProbesQuery(Paggination paggination)
        : base(paggination)
    {
    }
}

internal sealed class ListScheduleProbesQueryConfiguration : ICommandConfiguration<ListScheduleProbesQuery>
{
    public void Configure(CommandConfigurationBuilder<ListScheduleProbesQuery> builder) => builder.RequirePermissions(ScheduleProbePermissions.Read);
}

/// <summary>Reads through the tst context, so the scope filter keeps a probe out of the caller's reach out of sight.</summary>
internal sealed class GetScheduleProbeQueryHandler(TasksDbContext context) : IRequestHandler<GetScheduleProbeQuery, ErrorOr<ScheduleProbeResponse>>
{
    public async Task<ErrorOr<ScheduleProbeResponse>> Handle(GetScheduleProbeQuery request, CancellationToken cancellationToken)
    {
        ScheduleProbe? probe = await context.Probes.AsNoTracking().SingleOrDefaultAsync(found => found.Id == request.Id, cancellationToken);

        return probe is null ? Error.NotFound() : new ScheduleProbeResponse(probe.Id.Value, probe.LastNote ?? string.Empty);
    }
}

internal sealed class ListScheduleProbesQueryHandler(TasksDbContext context)
    : IRequestHandler<ListScheduleProbesQuery, ErrorOr<ListQueryResponse<ScheduleProbeListItem>>>
{
    public async Task<ErrorOr<ListQueryResponse<ScheduleProbeListItem>>> Handle(ListScheduleProbesQuery request, CancellationToken cancellationToken)
    {
        List<ScheduleProbe> probes = await context.Probes.AsNoTracking().ToListAsync(cancellationToken);
        IReadOnlyCollection<ScheduleProbeListItem> items =
            [.. probes.OrderBy(probe => probe.Id.Value).Select(probe => new ScheduleProbeListItem(probe.Id.Value, probe.LastNote ?? string.Empty))];

        return new ListQueryResponse<ScheduleProbeListItem>(items, items.Count);
    }
}

internal sealed class ScheduleProbeRepository(ITasksTestDbContext context)
    : EfRepository<ScheduleProbe, ScheduleProbeId>(context);

/// <summary>What the three Window configurations share: the routes, the two shown tasks' labels, every other probe task hidden.</summary>
internal static class ScheduleProbeViews
{
    /// <param name="view">The probe's view.</param>
    /// <param name="window">How Window is offered.</param>
    /// <param name="inlineSchedulable">Whether the schedulable Sweep and Refresh are offered inline too (else in their dialog).</param>
    public static void Configure(
        AggregateViewBuilder<ScheduleProbe> view, Action<CommandViewBuilder<ProbeWindowTaskCommand>> window, bool inlineSchedulable = false)
    {
        view.Routes(detail: id => $"/tst/probes/{id}");

        window(view.Command<ProbeWindowTaskCommand>().Label(ScheduleProbeTexts.Window));
        CommandViewBuilder<ProbeSweepTaskCommand> sweep = view.Command<ProbeSweepTaskCommand>().Label(ScheduleProbeTexts.Sweep);
        CommandViewBuilder<ProbeRefreshTaskCommand> refresh = view.Command<ProbeRefreshTaskCommand>().Label(ScheduleProbeTexts.Refresh);

        if (inlineSchedulable)
        {
            sweep.Edit(SerginEditMode.Inline);
            refresh.Edit(SerginEditMode.Inline);
        }

        view.Command<ProbeKickTaskCommand>().Label(ScheduleProbeTexts.Kick);
        view.Command<ProbeShiftTaskCommand>().Label(ScheduleProbeTexts.Shift);

        view.Command<TouchProbeCommand>().Hidden();
        view.Command<TouchOneProbeCommand>().Hidden();
        view.Command<StartOnlyProbeCommand>().Hidden();
        view.Command<SlowProbeCommand>().Hidden();
        view.Command<FixedProbeCommand>().Hidden();
        view.Command<PacedProbeCommand>().Hidden();
        view.Command<CappedProbeCommand>().Hidden();
        view.Command<LongTimedProbeCommand>().Hidden();
        view.Command<GatedTimedProbeCommand>().Hidden();
        view.Command<WrappingTimedProbeCommand>().Hidden();
        view.Command<CancelThenThrowProbeCommand>().Hidden();
        view.Command<ProgressProbeCommand>().Hidden();
    }
}

internal sealed class DialogScheduleProbeViewConfiguration : IAggregateViewConfiguration<ScheduleProbe>
{
    public void Configure(AggregateViewBuilder<ScheduleProbe> view) => ScheduleProbeViews.Configure(view, _ => { });
}

internal sealed class PageScheduleProbeViewConfiguration : IAggregateViewConfiguration<ScheduleProbe>
{
    public void Configure(AggregateViewBuilder<ScheduleProbe> view) =>
        ScheduleProbeViews.Configure(view, window => window.Edit(SerginEditMode.Page).PageRoute(id => $"/tst/probes/{id}/window"));
}

internal sealed class InlineScheduleProbeViewConfiguration : IAggregateViewConfiguration<ScheduleProbe>
{
    public void Configure(AggregateViewBuilder<ScheduleProbe> view) =>
        ScheduleProbeViews.Configure(view, window => window.Edit(SerginEditMode.Inline), inlineSchedulable: true);
}

/// <summary>
/// Puts the schedule probe's generated views into a tasks test host (<see cref="TaskTestHost"/>), configured by
/// <paramref name="viewConfiguration"/>: the two tasks and the two queries as a tst source, the probe's texts, the
/// window's validator and the probe repository it reads, and the query handlers over the tst context.
/// </summary>
internal static class ScheduleProbeViewHost
{
    public static Type[] Types { get; } =
    [
        typeof(ProbeWindowTaskCommandConfiguration),
        typeof(ProbeKickTaskCommandConfiguration),
        typeof(ProbeSweepTaskCommandConfiguration),
        typeof(ProbeRefreshTaskCommandConfiguration),
        typeof(ProbeShiftTaskCommandConfiguration),
        typeof(ProbeShiftTaskCommand),
        typeof(ProbeSweepTaskCommand),
        typeof(ProbeRefreshTaskCommand),
        typeof(CreateScheduleProbeCommand),
        typeof(GetScheduleProbeQueryConfiguration),
        typeof(ListScheduleProbesQueryConfiguration),
        typeof(ProbeWindowTaskCommand),
        typeof(ProbeKickTaskCommand),
        typeof(GetScheduleProbeQuery),
        typeof(ListScheduleProbesQuery),
    ];

    public static void AddTo(IServiceCollection services, Type viewConfiguration)
    {
        services.AddSingleton(ModuleConfigurationSource.FromTypesInSchema(TasksDbContext.Schema, Types));
        services.AddSingleton(TextSource.FromTypes("Tst", TextOrigin.Ui, typeof(ScheduleProbeTexts)));
        services.AddSingleton(AggregateViewSource.FromTypes("tst", viewConfiguration));

        services.AddTransient<IRepository<ScheduleProbe, ScheduleProbeId>, ScheduleProbeRepository>();
        services.AddSingleton<CreatedProbeRecorder>();
        services.AddTransient<IRequestHandler<CreateScheduleProbeCommand, ErrorOr<Success>>, CreateScheduleProbeCommandHandler>();
        services.AddScoped<IValidator<ProbeWindowTaskCommand>, ProbeWindowTaskCommandValidator>();
        services.AddScoped<IValidator<ProbeShiftTaskCommand>, ProbeShiftTaskCommandValidator>();
        services.AddSingleton<ShiftRecorder>();
        services.AddTransient<IRequestHandler<ProbeShiftTaskCommand, ErrorOr<Success>>, ProbeShiftTaskCommandHandler>();
        services.AddTransient<IRequestHandler<GetScheduleProbeQuery, ErrorOr<ScheduleProbeResponse>>, GetScheduleProbeQueryHandler>();
        services.AddTransient<IRequestHandler<ListScheduleProbesQuery, ErrorOr<ListQueryResponse<ScheduleProbeListItem>>>, ListScheduleProbesQueryHandler>();
    }
}
