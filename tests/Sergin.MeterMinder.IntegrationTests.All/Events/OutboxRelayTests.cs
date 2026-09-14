using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Outbox;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Events;

/// <summary>
/// The regression tests for the outbox, both halves, driven by hand. Producer side: raising a domain event
/// that has a registered translator writes an <c>outbox_messages</c> row in the same <c>SaveChangesAsync</c>
/// that persists the aggregate, and a domain event handler throwing leaves neither behind. Relay side, by
/// calling <c>IOutboxRelaySource.RelayOnceAsync</c> directly with the background service removed (see
/// <see cref="OutboxTestHost.RemoveRelayService"/>): a claimed row reaches
/// <c>IIntegrationEventHandler&lt;TEvent&gt;</c> consumers and is stamped processed; a throwing consumer
/// records an attempt with backoff instead of propagating; a redelivered message is skipped by the inbox; a
/// row at <c>MaxAttempts</c> is no longer claimed; a consumer's <c>ISender.Send</c> of a permissioned command
/// passes only because the relay seeded its system identity, and the outbox row that command produces names
/// the consumed message as its cause; and <c>PurgeAsync</c> drops processed rows past retention. The
/// background service itself is covered by <see cref="OutboxRelayServiceTests"/>.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class OutboxRelayTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    // Plain literals, not interpolations over TestEventsDbContext.Schema: ExecuteSqlRawAsync with an
    // interpolated string trips EF1002, and identifiers cannot be parameterized anyway.
    private const string UnprocessSql = "UPDATE test_events.outbox_messages SET processed_on_utc = NULL;";
    private const string ExhaustAttemptsSql = "UPDATE test_events.outbox_messages SET attempts = 10;";
    private const string AgeProcessedRowsSql =
        "UPDATE test_events.outbox_messages SET processed_on_utc = now() - interval '30 days'; "
        + "UPDATE test_events.inbox_messages SET processed_on_utc = now() - interval '30 days';";

    private WebApplicationFactory<Program> eventsFactory = default!;

    private IOutboxRelaySource TestSource =>
        eventsFactory.Services.GetServices<IOutboxRelaySource>().Single(source => source.Schema == TestEventsDbContext.Schema);

    public async Task InitializeAsync()
    {
        eventsFactory = OutboxTestHost.Create(factory, builder => builder.ConfigureServices(OutboxTestHost.RemoveRelayService));
        await OutboxTestHost.ResetSchemaAsync(eventsFactory);
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

    [Fact]
    public async Task RelayOnce_DeliversToHandler_AndStampsProcessed()
    {
        ResetConsumerState();
        TestAggregate aggregate = await SaveAggregateAsync("plain");

        int claimed = await TestSource.RelayOnceAsync(CancellationToken.None);

        Assert.Equal(1, claimed);

        using IServiceScope readScope = eventsFactory.Services.CreateScope();
        TestEventsDbContext readContext = readScope.ServiceProvider.GetRequiredService<TestEventsDbContext>();
        OutboxMessage row = await readContext.OutboxMessages.AsNoTracking().SingleAsync();

        (Guid? causationMessageId, string? correlationId, TestAggregateCreatedIntegrationEvent delivered) =
            Assert.Single(Recorded.Events);
        Assert.Equal(aggregate.Id, delivered.AggregateId);
        Assert.Equal(row.Id, causationMessageId);
        Assert.Equal(row.CorrelationId, correlationId);

        Assert.NotNull(row.ProcessedOnUtc);
        Assert.Null(row.Error);
        Assert.Equal(0, row.Attempts);

        InboxMessage inbox = await readContext.InboxMessages.AsNoTracking().SingleAsync();
        Assert.Equal(row.Id, inbox.MessageId);
        Assert.Equal(typeof(RecordingIntegrationHandler).FullName, inbox.Handler);
    }

    [Fact]
    public async Task RelayOnce_WhenHandlerThrows_RecordsAttemptAndBackoff()
    {
        ResetConsumerState();
        Failure.ShouldThrow = true;
        await SaveAggregateAsync("doomed");

        int claimed = await TestSource.RelayOnceAsync(CancellationToken.None);

        Assert.Equal(1, claimed);

        OutboxMessage row = await ReadSingleMessageAsync();
        Assert.Null(row.ProcessedOnUtc);
        Assert.Equal(1, row.Attempts);
        Assert.NotNull(row.Error);
        Assert.Contains("Simulated integration event handler failure", row.Error, StringComparison.Ordinal);

        // Backoff after the first failure is 2 s; a second of slack keeps the assertion honest without
        // racing the clock.
        Assert.NotNull(row.NextAttemptAt);
        Assert.True(row.NextAttemptAt > DateTime.UtcNow.AddSeconds(1));

        // Still inside the backoff window, so the row is not claimable yet.
        Assert.Equal(0, await TestSource.RelayOnceAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RelayOnce_RedeliveredMessage_IsSkippedByInbox()
    {
        ResetConsumerState();
        await SaveAggregateAsync("redelivered");

        Assert.Equal(1, await TestSource.RelayOnceAsync(CancellationToken.None));
        Assert.Single(Recorded.Events);

        // Simulate the crash-after-delivery-before-commit case: the row is back to unprocessed, but the
        // inbox row from the first delivery is still there.
        await ExecuteSqlAsync(UnprocessSql);

        Assert.Equal(1, await TestSource.RelayOnceAsync(CancellationToken.None));

        Assert.Single(Recorded.Events);

        OutboxMessage row = await ReadSingleMessageAsync();
        Assert.NotNull(row.ProcessedOnUtc);
        Assert.Equal(0, row.Attempts);
    }

    [Fact]
    public async Task RelayOnce_AtMaxAttempts_IsDeadLettered()
    {
        ResetConsumerState();
        await SaveAggregateAsync("dead");
        await ExecuteSqlAsync(ExhaustAttemptsSql);

        int claimed = await TestSource.RelayOnceAsync(CancellationToken.None);

        Assert.Equal(0, claimed);

        OutboxMessage row = await ReadSingleMessageAsync();
        Assert.Null(row.ProcessedOnUtc);
        Assert.Empty(Recorded.Events);
    }

    [Fact]
    public async Task RelayOnce_ConsumerRunsAsRelayUser_AndChainsCausation()
    {
        ResetConsumerState();
        TestAggregate parentAggregate = await SaveAggregateAsync(ChainingIntegrationHandler.ParentName);

        int claimed = await TestSource.RelayOnceAsync(CancellationToken.None);

        Assert.Equal(1, claimed);

        using IServiceScope readScope = eventsFactory.Services.CreateScope();
        TestEventsDbContext readContext = readScope.ServiceProvider.GetRequiredService<TestEventsDbContext>();

        List<OutboxMessage> rows = await readContext.OutboxMessages.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);

        OutboxMessage parent = Assert.Single(rows, row => row.Content.Contains(parentAggregate.Id.ToString(), StringComparison.Ordinal));
        OutboxMessage child = Assert.Single(rows, row => row.Id != parent.Id);

        Assert.NotNull(parent.ProcessedOnUtc);
        Assert.Equal(parent.Id, child.CausationId);
        Assert.Equal(parent.CorrelationId, child.CorrelationId);
        Assert.Null(child.ProcessedOnUtc);

        Assert.Equal(2, await readContext.Aggregates.CountAsync());
    }

    [Fact]
    public async Task Purge_RemovesProcessedRowsPastRetention()
    {
        ResetConsumerState();
        await SaveAggregateAsync("old");
        Assert.Equal(1, await TestSource.RelayOnceAsync(CancellationToken.None));
        await ExecuteSqlAsync(AgeProcessedRowsSql);

        await TestSource.PurgeAsync(CancellationToken.None);

        using IServiceScope readScope = eventsFactory.Services.CreateScope();
        TestEventsDbContext readContext = readScope.ServiceProvider.GetRequiredService<TestEventsDbContext>();
        Assert.Empty(await readContext.OutboxMessages.AsNoTracking().ToListAsync());
        Assert.Empty(await readContext.InboxMessages.AsNoTracking().ToListAsync());
    }

    private RecordedIntegrationEvents Recorded => eventsFactory.Services.GetRequiredService<RecordedIntegrationEvents>();

    private FailureSwitch Failure => eventsFactory.Services.GetRequiredService<FailureSwitch>();

    // The singletons are built per eventsFactory, so they are fresh per test already; resetting explicitly
    // keeps each test's precondition visible rather than implied by fixture lifetime.
    private void ResetConsumerState()
    {
        Recorded.Clear();
        Failure.ShouldThrow = false;
    }

    private Task<TestAggregate> SaveAggregateAsync(string name) => OutboxTestHost.SaveAggregateAsync(eventsFactory, name);

    private async Task<OutboxMessage> ReadSingleMessageAsync()
    {
        using IServiceScope scope = eventsFactory.Services.CreateScope();
        TestEventsDbContext context = scope.ServiceProvider.GetRequiredService<TestEventsDbContext>();

        return await context.OutboxMessages.AsNoTracking().SingleAsync();
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        using IServiceScope scope = eventsFactory.Services.CreateScope();
        TestEventsDbContext context = scope.ServiceProvider.GetRequiredService<TestEventsDbContext>();

        await context.Database.ExecuteSqlRawAsync(sql);
    }
}
