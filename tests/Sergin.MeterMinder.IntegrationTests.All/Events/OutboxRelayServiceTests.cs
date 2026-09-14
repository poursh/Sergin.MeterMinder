using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Sergin.SharedKernel.Application.Events.Integration;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Outbox;
using Sergin.SharedKernel.Infrastructure.Events.Integration;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Events;

/// <summary>
/// The background half of the relay: <c>OutboxRelayService</c> polling on its own delivers a written row
/// without anybody calling <c>RelayOnceAsync</c>, and the two things that must stop the host before it
/// serves a request — an integration event type missing its <c>[IntegrationEventName]</c>, and a
/// <c>Sergin:Outbox</c> value the relay cannot run with — do so naming the offender. The manual,
/// pass-by-pass relay coverage lives in <see cref="OutboxRelayTests"/>.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class OutboxRelayServiceTests(SerginWebApiFactory<Program> factory)
{
    private static readonly TimeSpan DeliveryTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PollDelay = TimeSpan.FromMilliseconds(100);

    [Fact]
    public async Task BackgroundRelay_DeliversWithoutManualTrigger()
    {
        await using WebApplicationFactory<Program> eventsFactory = OutboxTestHost.Create(
            factory, builder => builder.UseSetting("Sergin:Outbox:PollInterval", "00:00:00.500"));
        await OutboxTestHost.ResetSchemaAsync(eventsFactory);

        // The service starts with the host, before the schema reset above, so a pass may already have
        // drained whatever a previous test class left in test_events. Anything it delivered was recorded
        // before the reset could take the table locks it held, so clearing here leaves only this test's row.
        RecordedIntegrationEvents recorded = eventsFactory.Services.GetRequiredService<RecordedIntegrationEvents>();
        recorded.Clear();

        TestAggregate aggregate = await OutboxTestHost.SaveAggregateAsync(eventsFactory, "background");

        OutboxMessage row = await WaitForProcessedRowAsync(eventsFactory);

        (Guid? causationMessageId, string? correlationId, _) =
            Assert.Single(recorded.Events, entry => entry.Event.AggregateId == aggregate.Id);
        Assert.Equal(row.Id, causationMessageId);
        Assert.Equal(row.CorrelationId, correlationId);
        Assert.Null(row.Error);
        Assert.Equal(0, row.Attempts);
    }

    [Fact]
    public void HostStart_WithUnnamedIntegrationEvent_Throws()
    {
        WebApplicationFactory<Program> misconfigured = factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.AddSingleton<IIntegrationEventSource>(new TypesIntegrationEventSource(typeof(UnnamedIntegrationEvent)))));

        // CreateClient is what builds and starts the host, so the failure surfaces here.
        InvalidOperationException failure =
            Assert.Throws<InvalidOperationException>(() => misconfigured.CreateClient());

        Assert.Contains("[IntegrationEventName", failure.Message, StringComparison.Ordinal);
        Assert.Contains(typeof(UnnamedIntegrationEvent).FullName!, failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void HostStart_WithZeroBatchSize_FailsStartupNamingTheKey()
    {
        WebApplicationFactory<Program> misconfigured = factory.WithWebHostBuilder(builder =>
            builder.UseSetting("Sergin:Outbox:BatchSize", "0"));

        OptionsValidationException failure =
            Assert.Throws<OptionsValidationException>(() => misconfigured.CreateClient());

        Assert.Contains("Sergin:Outbox:BatchSize", failure.Message, StringComparison.Ordinal);
    }

    private static async Task<OutboxMessage> WaitForProcessedRowAsync(WebApplicationFactory<Program> eventsFactory)
    {
        using CancellationTokenSource timeout = new(DeliveryTimeout);

        while (true)
        {
            using IServiceScope scope = eventsFactory.Services.CreateScope();
            TestEventsDbContext context = scope.ServiceProvider.GetRequiredService<TestEventsDbContext>();
            OutboxMessage row = await context.OutboxMessages.AsNoTracking().SingleAsync(timeout.Token);

            if (row.ProcessedOnUtc is not null)
            {
                return row;
            }

            await Task.Delay(PollDelay, timeout.Token);
        }
    }

    // Nested on purpose: AssemblyIntegrationEventSource skips nested types, so the test assembly's own scan
    // (registered by OutboxTestHost) never sees this one — only the explicit TypesIntegrationEventSource does.
    private sealed record UnnamedIntegrationEvent(Guid Id, DateTime OccurredOnUtc) : IIntegrationEvent;
}
