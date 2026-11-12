using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.ScheduledTasks;
using Sergin.SharedKernel.Hosts.ScheduledTasks;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Infrastructure.ScheduledTasks.TickerQ;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.ScheduledTasks;

/// <summary>
/// The scheduled-tasks test host: the shared factory plus <see cref="ScheduledTasksDbContext"/>, registered through
/// <c>AddModuleDbContext</c> (which is what opts it in), and the reusable probe configurations. Later scheduled-task
/// tests extend it through <paramref name="configure"/>.
/// </summary>
internal static class ScheduledTaskTestHost
{
    // A plain literal: ExecuteSqlRawAsync with an interpolated string trips EF1002, and identifiers cannot be parameters.
    private const string ResetSchemaSql = "DROP SCHEMA IF EXISTS tst CASCADE; CREATE SCHEMA tst;";

    /// <summary>The reusable probe configurations, fed to the host as one test source.</summary>
    public static Type[] ProbeConfigurations { get; } =
    [
        typeof(ScheduleProbeAggregateFeatureConfiguration),
        typeof(TouchProbeCommandConfiguration),
        typeof(TouchProbeChildCommandConfiguration),
        typeof(TouchOneProbeCommandConfiguration),
    ];

    /// <param name="factory">The shared factory.</param>
    /// <param name="configure">The test's own services.</param>
    /// <param name="hostedServices">
    /// Keep the trigger rebuild, the purge loop and the run item recovery. Off by default: they run at host start, against whatever an earlier
    /// test class left in the shared tst schema, racing the reset and filling the test's engine with stale triggers. Only
    /// the tests of those two services keep them; without them, a host with no engine (a model test) still starts.
    /// </param>
    /// <param name="standInEngine">
    /// Register the engine the way a host does, through <c>AddSerginTickerQ</c>, with a blank licence path: the in-process
    /// stand-in and its worker, not a fake. Off by default: most tests drive a fake engine by hand.
    /// </param>
    public static WebApplicationFactory<Program> Create(
        SerginWebApiFactory<Program> factory, Action<IWebHostBuilder>? configure = null, bool hostedServices = false, bool standInEngine = false)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            if (standInEngine)
            {
                builder.UseSetting(TickerQTestStore.LicenseKey, string.Empty);
            }

            builder.ConfigureServices((context, services) =>
            {
                // The real host registers the stand-in engine itself; a test host starts with none, as these tests were written
                // for, and adds the engine it wants (a fake, or the stand-in through standInEngine).
                RemoveHostEngine(services);

                if (!hostedServices)
                {
                    RemoveHostedService<ScheduledTaskTriggerRebuildService>(services);
                    RemoveHostedService<ScheduledTaskPurgeService>(services);
                    RemoveHostedService<ScheduledRunItemRecoveryService>(services);
                }

                services.AddModuleDbContext<ScheduledTasksDbContext, IScheduledTasksTestDbContext, IScheduledTasksTestUnitOfWork>(
                    context.Configuration.GetSection("Sergin"), ScheduledTasksDbContext.Schema);

                services.AddSingleton(ModuleConfigurationSource.FromTypes(ProbeConfigurations));

                AddProbeToHostRegistry(services);

                if (standInEngine)
                {
                    IConfigurationSection sergin = context.Configuration.GetSection("Sergin");
                    services.AddSerginTickerQ(sergin, sergin.GetSection("ConnectionStrings")["Database"]!);
                }
            });

            configure?.Invoke(builder);
        });
    }

    public static async Task ResetSchemaAsync(WebApplicationFactory<Program> host)
    {
        using IServiceScope scope = host.Services.CreateScope();
        ScheduledTasksDbContext context = scope.ServiceProvider.GetRequiredService<ScheduledTasksDbContext>();

        // Not EnsureCreatedAsync: it no-ops once the database holds any table. CreateTablesAsync builds this model's.
        await context.Database.ExecuteSqlRawAsync(ResetSchemaSql);
        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }

    private static void RemoveHostEngine(IServiceCollection services)
    {
        foreach (ServiceDescriptor engine in services
            .Where(descriptor => descriptor.ServiceType == typeof(IScheduledTaskEngine)
                || descriptor.ServiceType == typeof(InProcessScheduledTaskEngine)
                || descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType?.Name == "InProcessRunItemWorker")
            .ToList())
        {
            services.Remove(engine);
        }
    }

    private static void RemoveHostedService<TService>(IServiceCollection services)
    {
        foreach (ServiceDescriptor hosted in services
            .Where(descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(TService))
            .ToList())
        {
            services.Remove(hosted);
        }
    }

    /// <summary>
    /// The probe is Scoped() in the tst model, and the startup guard refuses a type the model scopes but the host's
    /// registry does not know: a real module's configuration comes from its ConfigurationsAssembly, this test one does
    /// not. So the host's registry, whatever <c>AddSerginCore</c> built, is decorated with the probe's configuration.
    /// </summary>
    private static void AddProbeToHostRegistry(IServiceCollection services)
    {
        ServiceDescriptor host = services.Last(descriptor => descriptor.ServiceType == typeof(AggregateFeatureRegistry));
        services.Remove(host);
        services.AddSingleton(provider =>
            ((AggregateFeatureRegistry)(host.ImplementationInstance ?? host.ImplementationFactory!(provider)))
                .Including([typeof(ScheduleProbeAggregateFeatureConfiguration)]));
    }
}
