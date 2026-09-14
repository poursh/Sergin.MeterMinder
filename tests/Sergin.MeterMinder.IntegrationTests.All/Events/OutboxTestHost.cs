using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sergin.SharedKernel.Application.Events;
using Sergin.SharedKernel.Application.Events.Integration;
using Sergin.SharedKernel.Hosts.Outbox;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Infrastructure.Events.Integration;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Events;

/// <summary>
/// The outbox test host, shared by <see cref="OutboxRelayTests"/> and <see cref="OutboxRelayServiceTests"/>:
/// the shared factory plus <see cref="TestEventsDbContext"/>, the test-only translator, and the consumer
/// handlers, all added through <c>WithWebHostBuilder</c> the way <c>DomainEventDispatchTests</c> does it.
/// Everything under test — <c>EventDispatcherInterceptor</c>, <c>OutboxWriter</c>, <c>EfInbox</c>,
/// <c>OutboxRelaySource</c>, <c>OutboxRelayIdentity</c>, <c>OutboxRelayService</c> — is the real host wiring
/// from <c>AddSerginCore</c> and <c>AddModuleDbContext</c>; only the producer and the consumers are test-owned.
/// </summary>
internal static class OutboxTestHost
{
    // A plain literal, not an interpolation over TestEventsDbContext.Schema: ExecuteSqlRawAsync with an
    // interpolated string trips EF1002, and identifiers cannot be parameterized anyway.
    private const string ResetSchemaSql = "DROP SCHEMA IF EXISTS test_events CASCADE; CREATE SCHEMA test_events;";

    public static WebApplicationFactory<Program> Create(SerginWebApiFactory<Program> factory, Action<IWebHostBuilder>? configure = null)
    {
        return factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices((context, services) =>
            {
                services.AddModuleDbContext<TestEventsDbContext, ITestEventsDbContext, ITestEventsUnitOfWork>(
                    context.Configuration.GetSection("Sergin"), TestEventsDbContext.Schema);

                services.AddSingleton<IIntegrationEventSource>(new AssemblyIntegrationEventSource(typeof(OutboxTestHost).Assembly));
                services.AddTransient<IIntegrationEventTranslator<TestAggregateCreated>, TestAggregateCreatedTranslator>();

                services.AddScoped<RecordedEvents>();
                services.AddTransient<INotificationHandler<DomainEventNotification<TestAggregateCreated>>, ThrowingHandler>();

                // Consumer side. Singletons, because the relay delivers in a scope the test never sees; the
                // handlers are registered by hand as INotificationHandler<IntegrationEventNotification<T>> for
                // the same reason DomainEventDispatchTests registers its domain handlers that way — the MediatR
                // scan covers the modules' ApplicationAssembly only, not this test assembly. Recording goes
                // first so its inbox row is written before Throwing gets a chance to abort the publish.
                services.AddSingleton<RecordedIntegrationEvents>();
                services.AddSingleton<FailureSwitch>();
                services.AddTransient<INotificationHandler<IntegrationEventNotification<TestAggregateCreatedIntegrationEvent>>, RecordingIntegrationHandler>();
                services.AddTransient<INotificationHandler<IntegrationEventNotification<TestAggregateCreatedIntegrationEvent>>, ThrowingIntegrationHandler>();
                services.AddTransient<INotificationHandler<IntegrationEventNotification<TestAggregateCreatedIntegrationEvent>>, ChainingIntegrationHandler>();
                services.AddTransient<IRequestHandler<CreateChildAggregateCommand, ErrorOr<Guid>>, CreateChildAggregateCommandHandler>();
            });

            configure?.Invoke(builder);
        });
    }

    /// <summary>
    /// Drops the background relay so a test can drive <c>IOutboxRelaySource</c> by hand and assert on what
    /// one pass claimed. With the service left in, it would race every manual pass for the same rows —
    /// <c>FOR UPDATE SKIP LOCKED</c> makes that safe in production and unassertable in a test.
    /// </summary>
    public static void RemoveRelayService(IServiceCollection services)
    {
        ServiceDescriptor relay = services.Single(descriptor =>
            descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(OutboxRelayService));
        services.Remove(relay);
    }

    public static async Task ResetSchemaAsync(WebApplicationFactory<Program> eventsFactory)
    {
        using IServiceScope scope = eventsFactory.Services.CreateScope();
        TestEventsDbContext context = scope.ServiceProvider.GetRequiredService<TestEventsDbContext>();

        // Not EnsureCreatedAsync: it no-ops as soon as the database holds any table, and the modules'
        // migrations have already run by the time this host is up. CreateTablesAsync builds this model's
        // tables regardless.
        await context.Database.ExecuteSqlRawAsync(ResetSchemaSql);
        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }

    public static async Task<TestAggregate> SaveAggregateAsync(WebApplicationFactory<Program> eventsFactory, string name)
    {
        using IServiceScope scope = eventsFactory.Services.CreateScope();
        TestEventsDbContext context = scope.ServiceProvider.GetRequiredService<TestEventsDbContext>();

        var aggregate = TestAggregate.Create(name);
        context.Aggregates.Add(aggregate);
        await context.SaveChangesAsync();

        return aggregate;
    }
}
