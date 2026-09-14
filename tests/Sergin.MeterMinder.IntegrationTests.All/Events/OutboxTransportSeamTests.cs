using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Sergin.MeterMinder.DeviceManagement;
using Sergin.SharedKernel.Application.Events.Integration;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Outbox;
using Sergin.SharedKernel.Infrastructure.Events.Integration;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Modules;

namespace Sergin.MeterMinder.IntegrationTests.All.Events;

/// <summary>
/// The transport seam's contract, from both sides. Relay side: a pass hands each claimed row to whatever
/// <c>IIntegrationEventDispatcher</c> the host registered, as an <c>IntegrationEventEnvelope</c> that
/// mirrors the <c>outbox_messages</c> row field for field, and stamps the row processed when that call
/// returns — the relay itself never deserializes, opens a consumer scope, or reaches a handler. Composition
/// side: a dispatcher registered <em>before</em> <c>AddSerginCore</c> is the one the host resolves, and the
/// in-process dispatcher stays resolvable by its concrete type regardless, because a broker consumer service
/// needs it as its last mile. The in-process path itself — consumer scope, identity, causation, handlers —
/// is covered by <see cref="OutboxRelayTests"/>, which runs on the default dispatcher.
/// </summary>
/// <remarks>
/// The first test cannot register its fake the way a real host would (in <c>Program.cs</c>, before the
/// bootstrap call): <c>WebApplicationFactory</c>'s <c>ConfigureServices</c> hook runs after
/// <c>Program.cs</c> has already run <c>AddSerginCore</c>, so its <c>TryAddSingleton</c> has already placed
/// the in-process default. The test therefore <c>Replace</c>s that descriptor in the test-services hook,
/// and leaves "registered first wins" to the second test, which builds a bare <c>HostApplicationBuilder</c>
/// where the order is under its control.
/// </remarks>
[Collection(nameof(IntegrationTestCollection))]
public sealed class OutboxTransportSeamTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private readonly RecordingDispatcher transport = new();
    private WebApplicationFactory<Program> eventsFactory = default!;

    private IOutboxRelaySource TestSource =>
        eventsFactory.Services.GetServices<IOutboxRelaySource>().Single(source => source.Schema == TestEventsDbContext.Schema);

    public async Task InitializeAsync()
    {
        eventsFactory = OutboxTestHost.Create(factory, builder => builder.ConfigureServices(services =>
        {
            OutboxTestHost.RemoveRelayService(services);
            services.Replace(ServiceDescriptor.Singleton<IIntegrationEventDispatcher>(transport));
        }));
        await OutboxTestHost.ResetSchemaAsync(eventsFactory);
    }

    public async Task DisposeAsync()
    {
        await eventsFactory.DisposeAsync();
    }

    [Fact]
    public async Task RelayOnce_HandsRowToRegisteredDispatcher_AsEnvelope()
    {
        await OutboxTestHost.SaveAggregateAsync(eventsFactory, "seam");

        int claimed = await TestSource.RelayOnceAsync(CancellationToken.None);

        Assert.Equal(1, claimed);

        using IServiceScope readScope = eventsFactory.Services.CreateScope();
        TestEventsDbContext readContext = readScope.ServiceProvider.GetRequiredService<TestEventsDbContext>();
        OutboxMessage row = await readContext.OutboxMessages.AsNoTracking().SingleAsync();

        IntegrationEventEnvelope envelope = Assert.Single(transport.Envelopes);
        Assert.Equal(row.Id, envelope.MessageId);
        Assert.Equal("test_events.aggregate.created.v1", envelope.Type);
        Assert.Equal(row.Type, envelope.Type);
        Assert.Equal(row.Content, envelope.Content);
        Assert.Equal(row.OccurredOnUtc, envelope.OccurredOnUtc);
        Assert.Equal(row.CorrelationId, envelope.CorrelationId);
        Assert.Equal(row.CausationId, envelope.CausationId);

        Assert.NotNull(row.ProcessedOnUtc);
        Assert.Null(row.Error);
        Assert.Equal(0, row.Attempts);

        // The fake never delivered anything, so no consumer ran: the relay's only job was the hand-off.
        Assert.Empty(await readContext.InboxMessages.AsNoTracking().ToListAsync());
    }

    [Fact]
    public void AddSerginCore_KeepsADispatcherRegisteredBeforeIt_AndStillExposesTheInProcessOne()
    {
        HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Sergin:ConnectionStrings:Database"] = "Host=localhost;Database=never-opened",
        });

        RecordingDispatcher registeredFirst = new();
        builder.Services.AddSingleton<IIntegrationEventDispatcher>(registeredFirst);

        IReadOnlyCollection<ISerginModule> modules = [new DeviceManagementModule()];
        builder.AddSerginCore(modules);

        using IHost host = builder.Build();

        Assert.Same(registeredFirst, host.Services.GetRequiredService<IIntegrationEventDispatcher>());
        Assert.NotNull(host.Services.GetRequiredService<InProcessIntegrationEventDispatcher>());
    }

    /// <summary>
    /// A transport that only records. It never deserializes or publishes, so a message it "delivers" reaches
    /// no handler — which is exactly what lets the relay-side test tell the hand-off apart from the last mile.
    /// </summary>
    private sealed class RecordingDispatcher : IIntegrationEventDispatcher
    {
        private readonly List<IntegrationEventEnvelope> envelopes = [];

        public IReadOnlyList<IntegrationEventEnvelope> Envelopes => envelopes;

        public Task DispatchAsync(IntegrationEventEnvelope envelope, CancellationToken cancellationToken)
        {
            envelopes.Add(envelope);
            return Task.CompletedTask;
        }
    }
}
