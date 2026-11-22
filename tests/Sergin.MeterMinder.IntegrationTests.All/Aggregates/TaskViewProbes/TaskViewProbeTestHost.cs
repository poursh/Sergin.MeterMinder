using System.Collections.Concurrent;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.IntegrationTests.All.Tasks;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Commands.Types;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates;
using Sergin.SharedKernel.Presentation.Blazor.Aggregates.Configuration;

namespace Sergin.MeterMinder.IntegrationTests.All.Aggregates.TaskViewProbes;

// A test-only root whose generated views place tasks by type: two tasks over one record (one Schedulable(), one not), a
// task over the collection (Schedulable()), and a plain transition beside them. Its tasks are in the tst schema, so they
// start runs in the tasks test host's store. Only TaskViewProbeTestHost registers it, into a test host built on TaskTestHost.

internal sealed record TaskViewProbeId(Guid Value);

internal sealed class TaskViewProbe : AggregateRoot<TaskViewProbeId>;

internal static class TaskViewProbePermissions
{
    public static PermissionDefinition Read { get; } = PermissionDefinition.Create("permission.tst.task-view-probes.read", "View task view probes");

    public static PermissionDefinition Run { get; } = PermissionDefinition.Create("permission.tst.task-view-probes.run", "Run task view probes");
}

public static class TaskViewProbeTexts
{
    public static TextCategory Category { get; } = TextCategory.Create("Tst", "TaskViewProbes");

    public static TextDefinition Singular { get; } = Category.Text("Singular", "Task view probe");

    public static TextDefinition Title { get; } = Category.Text("Title", "Task view probes");

    public static TextDefinition Ping { get; } = Category.Text("Ping", "Ping probe");

    public static TextDefinition Sweep { get; } = Category.Text("Sweep", "Sweep probe");

    public static TextDefinition Refresh { get; } = Category.Text("Refresh", "Refresh probes");

    public static TextDefinition Retitle { get; } = Category.Text("Retitle", "Retitle probe");

    public static FieldText Name { get; } = Category.Field(nameof(Name), "Name");
}

internal sealed class TaskViewProbeAggregateFeatureConfiguration : IAggregateFeatureConfiguration<TaskViewProbe>
{
    public void Configure(AggregateFeatureBuilder<TaskViewProbe> builder) =>
        builder.Permissions(TaskViewProbePermissions.Read, TaskViewProbePermissions.Run)
            .Texts(TaskViewProbeTexts.Category)
            .Named(TaskViewProbeTexts.Singular);
}

internal sealed record TaskViewProbeResponse(Guid Id, string Name);

internal sealed record GetTaskViewProbeQuery(TaskViewProbeId Id) : IQuery<TaskViewProbeResponse>, IGetOneQuery<TaskViewProbe, TaskViewProbeId>;

internal sealed record TaskViewProbeListItem(Guid Id, string Name);

internal sealed record ListTaskViewProbesQuery : ListQuery<TaskViewProbeListItem>, IListOf<TaskViewProbe>
{
    public ListTaskViewProbesQuery(Paggination paggination)
        : base(paggination)
    {
    }
}

/// <summary>A task over one record that is not <c>Schedulable()</c>: started, never scheduled.</summary>
internal sealed record PingTaskViewProbeCommand(TaskViewProbeId Id) : ITaskCommand<TaskViewProbe, TaskViewProbeId>, ISelectable;

internal sealed class PingTaskViewProbeCommandConfiguration : ICommandConfiguration<PingTaskViewProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<PingTaskViewProbeCommand> builder) =>
        builder.RequirePermissions(TaskViewProbePermissions.Run).Task(task => task.Name("tst.task-view-probes.ping"));
}

/// <summary>A <c>Schedulable()</c> task over one record.</summary>
internal sealed record SweepTaskViewProbeCommand(TaskViewProbeId Id) : ITaskCommand<TaskViewProbe, TaskViewProbeId>, ISelectable;

internal sealed class SweepTaskViewProbeCommandConfiguration : ICommandConfiguration<SweepTaskViewProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<SweepTaskViewProbeCommand> builder) =>
        builder.RequirePermissions(TaskViewProbePermissions.Run).Task(task => task.Schedulable().Name("tst.task-view-probes.sweep"));
}

/// <summary>A <c>Schedulable()</c> task over the collection: no record, one run per scope at a time.</summary>
internal sealed record RefreshTaskViewProbesCommand : ITaskCommand<TaskViewProbe>;

internal sealed class RefreshTaskViewProbesCommandConfiguration : ICommandConfiguration<RefreshTaskViewProbesCommand>
{
    public void Configure(CommandConfigurationBuilder<RefreshTaskViewProbesCommand> builder) =>
        builder.RequirePermissions(TaskViewProbePermissions.Run).Task(task => task.Schedulable().Name("tst.task-view-probes.refresh"));
}

/// <summary>A command that is not a task: it never offers Schedule… nor shows a spinner.</summary>
internal sealed record RetitleTaskViewProbeCommand(TaskViewProbeId Id) : ICommand, ITransitionCommand<TaskViewProbe, TaskViewProbeId>;

internal sealed class RetitleTaskViewProbeCommandConfiguration : ICommandConfiguration<RetitleTaskViewProbeCommand>
{
    public void Configure(CommandConfigurationBuilder<RetitleTaskViewProbeCommand> builder) =>
        builder.RequirePermissions(TaskViewProbePermissions.Run);
}

internal sealed class TaskViewProbeViewConfiguration : IAggregateViewConfiguration<TaskViewProbe>
{
    public void Configure(AggregateViewBuilder<TaskViewProbe> view)
    {
        view.Title(TaskViewProbeTexts.Title)
            .Routes("/tst/task-view-probes", id => $"/tst/task-view-probes/{id}");

        view.Command<PingTaskViewProbeCommand>().Label(TaskViewProbeTexts.Ping);
        view.Command<SweepTaskViewProbeCommand>().Label(TaskViewProbeTexts.Sweep);
        view.Command<RefreshTaskViewProbesCommand>().Label(TaskViewProbeTexts.Refresh);
        view.Command<RetitleTaskViewProbeCommand>().Label(TaskViewProbeTexts.Retitle);
    }
}

/// <summary>The probe records the two queries read, by id; nothing reaches a database.</summary>
internal sealed class TaskViewProbeStore
{
    private readonly ConcurrentDictionary<Guid, TaskViewProbeResponse> records = new();

    public Guid Add(string name)
    {
        var id = Guid.CreateVersion7();
        records[id] = new TaskViewProbeResponse(id, name);
        return id;
    }

    public TaskViewProbeResponse? Find(Guid id) => records.GetValueOrDefault(id);

    public IReadOnlyCollection<TaskViewProbeListItem> All => [.. records.Values.OrderBy(record => record.Id).Select(record => new TaskViewProbeListItem(record.Id, record.Name))];
}

internal sealed class GetTaskViewProbeQueryHandler(TaskViewProbeStore store) : IRequestHandler<GetTaskViewProbeQuery, ErrorOr<TaskViewProbeResponse>>
{
    public Task<ErrorOr<TaskViewProbeResponse>> Handle(GetTaskViewProbeQuery request, CancellationToken cancellationToken) =>
        Task.FromResult<ErrorOr<TaskViewProbeResponse>>(store.Find(request.Id.Value) is { } found ? found : Error.NotFound());
}

internal sealed class ListTaskViewProbesQueryHandler(TaskViewProbeStore store)
    : IRequestHandler<ListTaskViewProbesQuery, ErrorOr<ListQueryResponse<TaskViewProbeListItem>>>
{
    public Task<ErrorOr<ListQueryResponse<TaskViewProbeListItem>>> Handle(ListTaskViewProbesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyCollection<TaskViewProbeListItem> all = store.All;

        return Task.FromResult<ErrorOr<ListQueryResponse<TaskViewProbeListItem>>>(new ListQueryResponse<TaskViewProbeListItem>(all, all.Count));
    }
}

/// <summary>
/// Puts the task view probe into a tasks test host (<see cref="TaskTestHost"/>): its requests and configurations as a tst
/// source, so its tasks start runs in the tst store; its texts, its view configuration, its in-memory query handlers. As
/// for the view probe, only the view registry is built with the probe's features included.
/// </summary>
internal static class TaskViewProbeTestHost
{
    public static Type[] Types { get; } =
    [
        typeof(TaskViewProbeAggregateFeatureConfiguration),
        typeof(PingTaskViewProbeCommandConfiguration),
        typeof(SweepTaskViewProbeCommandConfiguration),
        typeof(RefreshTaskViewProbesCommandConfiguration),
        typeof(RetitleTaskViewProbeCommandConfiguration),
        typeof(GetTaskViewProbeQuery),
        typeof(ListTaskViewProbesQuery),
        typeof(PingTaskViewProbeCommand),
        typeof(SweepTaskViewProbeCommand),
        typeof(RefreshTaskViewProbesCommand),
        typeof(RetitleTaskViewProbeCommand),
    ];

    public static void AddTo(IServiceCollection services)
    {
        services.AddSingleton(ModuleConfigurationSource.FromTypesInSchema(TasksDbContext.Schema, Types));
        services.AddSingleton(TextSource.FromTypes("Tst", TextOrigin.Ui, typeof(TaskViewProbeTexts)));
        services.AddSingleton(AggregateViewSource.FromTypes("tst", typeof(TaskViewProbeViewConfiguration)));

        ServiceDescriptor views = services.Last(descriptor => descriptor.ServiceType == typeof(AggregateViewRegistry));
        services.Remove(views);
        services.AddSingleton(provider => AggregateViewRegistry.Build(
            provider.GetServices<AggregateViewSource>(),
            provider.GetRequiredService<CommandConfigurationRegistry>(),
            provider.GetRequiredService<AggregateFeatureRegistry>().Including([typeof(TaskViewProbeAggregateFeatureConfiguration)]),
            provider.GetRequiredService<TextCatalog>()));

        services.AddSingleton<TaskViewProbeStore>();
        services.AddTransient<IRequestHandler<GetTaskViewProbeQuery, ErrorOr<TaskViewProbeResponse>>, GetTaskViewProbeQueryHandler>();
        services.AddTransient<IRequestHandler<ListTaskViewProbesQuery, ErrorOr<ListQueryResponse<TaskViewProbeListItem>>>, ListTaskViewProbesQueryHandler>();
    }
}
