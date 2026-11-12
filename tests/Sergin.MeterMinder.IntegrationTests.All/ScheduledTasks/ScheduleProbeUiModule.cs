using System.Reflection;
using ErrorOr;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Sergin.SharedKernel.Application.Localizations;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Modules;
using Sergin.SharedKernel.Presentation.Blazor.Modules;
using Sergin.SharedKernel.Presentation.Blazor.Times;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>The words of the probe page, in a test-only <c>tst</c> source beside the catalog's own.</summary>
public static class ScheduleProbeUiTexts
{
    private static readonly TextCategory category = TextCategory.Create("Tst", "Probes");

    public static TextDefinition Title { get; } = category.Text("Title", "Probe");

    public static TextDefinition Note { get; } = category.Text("Note", "Note");

    public static TextDefinition CommandTitle { get; } = category.Text("CommandTitle", "Touch the probe");
}

/// <summary>
/// A test-only web UI module for schema <c>tst</c>: it exists so the probe page's <c>/tst/probes/{id}</c> route passes
/// the schema-prefix guard and reaches the router's assemblies. Only the host's <see cref="SerginUiModuleCatalog"/> knows
/// it — the real host's module list is untouched — and it has no services, no migrations and no navigation.
/// </summary>
internal sealed class ScheduleProbeUiModule : ISerginWebUiModule
{
    public string Name => "Tst";

    public string Schema => ScheduledTasksDbContext.Schema;

    public Assembly ApplicationAssembly => typeof(ScheduleProbeUiModule).Assembly;

    public Assembly ContractsAssembly => typeof(ScheduleProbeUiModule).Assembly;

    public Assembly ConfigurationsAssembly => typeof(ScheduleProbeUiModule).Assembly;

    public Assembly UiAssembly => typeof(ScheduleProbeUiModule).Assembly;

    public SerginNavGroup NavGroup => SerginNavGroups.Administration;

    public IReadOnlyCollection<SerginNavItem> NavItems => [];

    public void AddServices(IServiceCollection services, IConfigurationSection configuration)
    {
    }

    public Task MigrateAsync(IServiceProvider services) => Task.CompletedTask;

    /// <summary>Puts the module in the host's UI catalogue and its texts in the text catalog.</summary>
    public static void AddTo(IServiceCollection services)
    {
        ServiceDescriptor registered = services.Last(descriptor => descriptor.ServiceType == typeof(SerginUiModuleCatalog));
        var catalog = (SerginUiModuleCatalog)registered.ImplementationInstance!;

        services.Remove(registered);
        services.AddSingleton(new SerginUiModuleCatalog([.. catalog.Modules, new ScheduleProbeUiModule()]));
        services.AddSingleton(TextSource.FromTypes("Tst", TextOrigin.Ui, typeof(ScheduleProbeUiTexts)));
    }
}

/// <summary>
/// The user every prerender runs as: the host's own (the development user, which builds the scenario) until a test sets
/// <see cref="Current"/> to a caller it granted, so a request runs as exactly that caller.
/// </summary>
internal sealed class ProbeUserContextFactory(IUserContextFactory inner) : IUserContextFactory
{
    public IUserContext? Current { get; set; }

    public IUserContext CreateUserContext() => Current ?? inner.CreateUserContext();

    public static void AddTo(IServiceCollection services)
    {
        ServiceDescriptor original = services.Last(descriptor => descriptor.ServiceType == typeof(IUserContextFactory));

        services.AddSingleton(provider => new ProbeUserContextFactory(
            original.ImplementationInstance as IUserContextFactory
            ?? (original.ImplementationFactory is { } create
                ? (IUserContextFactory)create(provider)
                : (IUserContextFactory)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!))));
        services.Replace(ServiceDescriptor.Singleton<IUserContextFactory>(provider => provider.GetRequiredService<ProbeUserContextFactory>()));
    }
}

/// <summary>The probe's handler-side services a schedule dialog's run would reach, registered as the query tests do.</summary>
internal static class ProbeHandlers
{
    public static void AddTo(IServiceCollection services)
    {
        services.AddSingleton<FakeScheduledTaskEngine>();
        services.Replace(ServiceDescriptor.Singleton<IScheduledTaskEngine>(provider => provider.GetRequiredService<FakeScheduledTaskEngine>()));
        services.AddSingleton<ProbeFailureSwitch>();
        services.AddSingleton<ProbeSendRecorder>();
        services.AddTransient<IRequestHandler<TouchProbeCommand, ErrorOr<TouchProbeResponse>>, TouchProbeCommandHandler>();
        services.AddScoped<FluentValidation.IValidator<TouchProbeCommand>, TouchProbeCommandValidator>();
    }
}

/// <summary>A viewer zone that needs no browser, so a component renders outside a circuit.</summary>
internal sealed class FixedTimeZoneStore : IUiTimeZoneStore
{
    public ValueTask<TimeZoneInfo> GetTimeZoneAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(TimeZoneInfo.Utc);
}
