using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Events;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Events;

/// <summary>
/// The regression tests for the domain event pipe: an aggregate's raised events reach
/// <c>IDomainEventHandler&lt;TEvent&gt;</c> implementations from inside <c>SaveChangesAsync</c>, before the
/// transaction commits, on the same scoped <c>DbContext</c>. Everything under test — the interceptor,
/// <c>DefaultEventDispatcher</c>, MediatR — is the real host wiring from <c>AddSerginCore</c>; only the
/// producer (<see cref="TestEventsDbContext"/>) and the handlers are test-owned, added on top of the shared
/// factory through <c>WithWebHostBuilder</c>. The handlers are registered by hand as
/// <c>INotificationHandler&lt;DomainEventNotification&lt;T&gt;&gt;</c> because the MediatR scan covers the
/// modules' <c>ApplicationAssembly</c> only, not this test assembly — the same precedent
/// <c>DeviceGrpcRoundTripTests</c> sets for <c>RemoteForwardingHandler</c>.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DomainEventDispatchTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    // A plain literal, not an interpolation over TestEventsDbContext.Schema: ExecuteSqlRawAsync with an
    // interpolated string trips EF1002, and identifiers cannot be parameterized anyway.
    private const string ResetSchemaSql = "DROP SCHEMA IF EXISTS test_events CASCADE; CREATE SCHEMA test_events;";

    private WebApplicationFactory<Program> eventsFactory = default!;

    public async Task InitializeAsync()
    {
        eventsFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices((context, services) =>
        {
            services.AddModuleDbContext<TestEventsDbContext, ITestEventsDbContext, ITestEventsUnitOfWork>(
                context.Configuration.GetSection("Sergin"), TestEventsDbContext.Schema);

            services.AddScoped<RecordedEvents>();
            services.AddTransient<INotificationHandler<DomainEventNotification<TestAggregateCreated>>, RecordingHandler>();
            services.AddTransient<INotificationHandler<DomainEventNotification<TestAggregateCreated>>, ThrowingHandler>();
            services.AddTransient<INotificationHandler<DomainEventNotification<TestAggregateCreated>>, CascadingHandler>();
        }));

        using IServiceScope scope = eventsFactory.Services.CreateScope();
        TestEventsDbContext context = scope.ServiceProvider.GetRequiredService<TestEventsDbContext>();

        // Not EnsureCreatedAsync: it no-ops as soon as the database holds any table, and the modules'
        // migrations have already run by the time this host is up. CreateTablesAsync builds this model's
        // tables regardless.
        await context.Database.ExecuteSqlRawAsync(ResetSchemaSql);
        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }

    public async Task DisposeAsync()
    {
        await eventsFactory.DisposeAsync();
    }

    [Fact]
    public async Task SaveChangesAsync_DispatchesRaisedEvent_AndClearsIt()
    {
        using IServiceScope scope = eventsFactory.Services.CreateScope();
        TestEventsDbContext context = scope.ServiceProvider.GetRequiredService<TestEventsDbContext>();
        RecordedEvents recorded = scope.ServiceProvider.GetRequiredService<RecordedEvents>();

        var aggregate = TestAggregate.Create("plain");
        context.Aggregates.Add(aggregate);

        await context.SaveChangesAsync();

        TestAggregateCreated dispatched = Assert.Single(recorded.Events);
        Assert.Equal(aggregate.Id, dispatched.AggregateId);
        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public async Task SaveChangesAsync_WhenHandlerThrows_PersistsNothing()
    {
        Guid id;

        using (IServiceScope scope = eventsFactory.Services.CreateScope())
        {
            TestEventsDbContext context = scope.ServiceProvider.GetRequiredService<TestEventsDbContext>();
            RecordedEvents recorded = scope.ServiceProvider.GetRequiredService<RecordedEvents>();
            recorded.FailNext = true;

            var aggregate = TestAggregate.Create("doomed");
            id = aggregate.Id;
            context.Aggregates.Add(aggregate);

            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        using IServiceScope readScope = eventsFactory.Services.CreateScope();
        TestEventsDbContext readContext = readScope.ServiceProvider.GetRequiredService<TestEventsDbContext>();

        Assert.False(await readContext.Aggregates.AnyAsync(x => x.Id == id));
    }

    [Fact]
    public async Task SaveChangesAsync_EntitiesAddedByHandler_PersistInSameSave()
    {
        Guid parentId;
        IReadOnlyList<TestAggregateCreated> dispatched;

        using (IServiceScope scope = eventsFactory.Services.CreateScope())
        {
            TestEventsDbContext context = scope.ServiceProvider.GetRequiredService<TestEventsDbContext>();
            RecordedEvents recorded = scope.ServiceProvider.GetRequiredService<RecordedEvents>();

            var parent = TestAggregate.Create(CascadingHandler.ParentName);
            parentId = parent.Id;
            context.Aggregates.Add(parent);

            await context.SaveChangesAsync();

            dispatched = recorded.Events;
        }

        Assert.Equal(2, dispatched.Count);
        Assert.Contains(dispatched, x => x.Name == CascadingHandler.ParentName && x.AggregateId == parentId);
        Assert.Contains(dispatched, x => x.Name == CascadingHandler.ChildName);

        using IServiceScope readScope = eventsFactory.Services.CreateScope();
        TestEventsDbContext readContext = readScope.ServiceProvider.GetRequiredService<TestEventsDbContext>();

        Assert.True(await readContext.Aggregates.AnyAsync(x => x.Id == parentId));
        Assert.True(await readContext.Aggregates.AnyAsync(x => x.Name == CascadingHandler.ChildName));
    }

    [Fact]
    public void SaveChanges_Sync_WithPendingEvents_Throws()
    {
        using IServiceScope scope = eventsFactory.Services.CreateScope();
        TestEventsDbContext context = scope.ServiceProvider.GetRequiredService<TestEventsDbContext>();

        context.Aggregates.Add(TestAggregate.Create("sync"));

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Contains(nameof(TestAggregate), exception.Message, StringComparison.Ordinal);
        Assert.Contains("SaveChangesAsync", exception.Message, StringComparison.Ordinal);
    }
}
