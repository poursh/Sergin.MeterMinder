using MediatR;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Events;
using Sergin.SharedKernel.Application.Events.Integration;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Outbox;
using Sergin.SharedKernel.Infrastructure.Events.Integration;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Events;

/// <summary>
/// The producer-side regression tests for the outbox: raising a domain event that has a registered
/// translator writes an <c>outbox_messages</c> row in the same <c>SaveChangesAsync</c> that persists the
/// aggregate, and a domain event handler throwing leaves neither the aggregate nor the outbox row behind.
/// Everything under test — <c>EventDispatcherInterceptor</c>, <c>OutboxWriter</c>, <c>EfInbox</c> — is the
/// real host wiring from <c>AddSerginCore</c>; only the producer (<see cref="TestEventsDbContext"/>), the
/// translator and the domain handler are test-owned, added on top of the shared factory through
/// <c>WithWebHostBuilder</c>, mirroring <c>DomainEventDispatchTests</c>. The relay itself — claiming and
/// dispatching a written row — is out of scope here; its own registrations and test coverage arrive with the
/// relay in a later task.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class OutboxRelayTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    // A plain literal, not an interpolation over TestEventsDbContext.Schema: ExecuteSqlRawAsync with an
    // interpolated string trips EF1002, and identifiers cannot be parameterized anyway.
    private const string ResetSchemaSql = "DROP SCHEMA IF EXISTS test_events CASCADE; CREATE SCHEMA test_events;";

    private WebApplicationFactory<Program> eventsFactory = default!;

    public async Task InitializeAsync()
    {
        eventsFactory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Sergin:Outbox:PollInterval", "00:00:00.500");

            builder.ConfigureServices((context, services) =>
            {
                services.AddModuleDbContext<TestEventsDbContext, ITestEventsDbContext, ITestEventsUnitOfWork>(
                    context.Configuration.GetSection("Sergin"), TestEventsDbContext.Schema);

                services.AddSingleton<IIntegrationEventSource>(new AssemblyIntegrationEventSource(typeof(OutboxRelayTests).Assembly));
                services.AddTransient<IIntegrationEventTranslator<TestAggregateCreated>, TestAggregateCreatedTranslator>();

                services.AddScoped<RecordedEvents>();
                services.AddTransient<INotificationHandler<DomainEventNotification<TestAggregateCreated>>, ThrowingHandler>();

                // Relay-side registrations (IOutboxRelayIdentity, OutboxRelayService, the relay source itself)
                // arrive with the relay tests in a later task.
            });
        });

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
    public async Task SaveChangesAsync_WithTranslator_WritesOutboxRow_InSameSave()
    {
        Guid aggregateId;

        using (IServiceScope scope = eventsFactory.Services.CreateScope())
        {
            TestEventsDbContext context = scope.ServiceProvider.GetRequiredService<TestEventsDbContext>();

            var aggregate = TestAggregate.Create("plain");
            aggregateId = aggregate.Id;
            context.Aggregates.Add(aggregate);

            await context.SaveChangesAsync();
        }

        using IServiceScope readScope = eventsFactory.Services.CreateScope();
        TestEventsDbContext readContext = readScope.ServiceProvider.GetRequiredService<TestEventsDbContext>();

        OutboxMessage message = await readContext.OutboxMessages.AsNoTracking().SingleAsync();

        Assert.Equal("test_events.aggregate.created.v1", message.Type);
        Assert.Contains(aggregateId.ToString(), message.Content, StringComparison.Ordinal);
        Assert.Null(message.ProcessedOnUtc);
        Assert.False(string.IsNullOrEmpty(message.CorrelationId));
        Assert.Null(message.CausationId);
        Assert.Equal(0, message.Attempts);
        Assert.Null(message.NextAttemptAt);
        Assert.Null(message.Error);
        Assert.Equal(new DateTime(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc), message.OccurredOnUtc);

        Assert.Equal(1, await readContext.Aggregates.CountAsync());
    }

    [Fact]
    public async Task SaveChangesAsync_WhenDomainHandlerThrows_WritesNoOutboxRow()
    {
        using (IServiceScope scope = eventsFactory.Services.CreateScope())
        {
            TestEventsDbContext context = scope.ServiceProvider.GetRequiredService<TestEventsDbContext>();
            RecordedEvents recorded = scope.ServiceProvider.GetRequiredService<RecordedEvents>();
            recorded.FailNext = true;

            var aggregate = TestAggregate.Create("doomed");
            context.Aggregates.Add(aggregate);

            await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        }

        using IServiceScope readScope = eventsFactory.Services.CreateScope();
        TestEventsDbContext readContext = readScope.ServiceProvider.GetRequiredService<TestEventsDbContext>();

        Assert.Empty(await readContext.Aggregates.ToListAsync());
        Assert.Empty(await readContext.OutboxMessages.ToListAsync());
    }
}
