using Microsoft.EntityFrameworkCore;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Outbox;

namespace Sergin.MeterMinder.IntegrationTests.All.Events;

/// <summary>
/// A test-only module: one aggregate that raises one event on creation, mapped into its own
/// <c>test_events</c> schema through the same <c>AddModuleDbContext</c> helper the real modules use. It
/// exists because no aggregate in DeviceManagement or UserAccess raises a domain event yet, and the
/// dispatch pipe (<c>EventDispatcherInterceptor</c> → <c>IEventDispatcher</c> → handlers) needs a producer
/// to be exercised end to end. It also opts into <see cref="IOutboxDbContext"/> so
/// <c>OutboxRelayTests</c> can prove the same interceptor writes an outbox row in the same save.
/// </summary>
internal interface ITestEventsDbContext : IDbContext;

internal interface ITestEventsUnitOfWork : IUnitOfWork;

internal sealed class TestEventsDbContext(DbContextOptions<TestEventsDbContext> options)
    : SerginDbContext(options), ITestEventsDbContext, ITestEventsUnitOfWork, IOutboxDbContext
{
    public const string Schema = "test_events";

    public DbSet<TestAggregate> Aggregates => Set<TestAggregate>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<TestAggregate>(aggregate =>
        {
            aggregate.ToTable("aggregates");
            aggregate.HasKey(x => x.Id);
            aggregate.Property(x => x.Name);
        });

        modelBuilder.ApplyOutbox();

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class TestAggregate : AggregateRoot<Guid>
{
    private TestAggregate()
    {
    }

    public string Name { get; private set; } = string.Empty;

    public static TestAggregate Create(string name)
    {
        TestAggregate aggregate = new()
        {
            Id = Guid.CreateVersion7(),
            Name = name
        };

        aggregate.Raise(new TestAggregateCreated(
            Guid.CreateVersion7(),
            new DateTime(2026, 9, 13, 0, 0, 0, DateTimeKind.Utc),
            aggregate.Id,
            name));

        return aggregate;
    }
}

internal sealed record TestAggregateCreated(Guid Id, DateTime OccurredOnUtc, Guid AggregateId, string Name) : IDomainEvent;
