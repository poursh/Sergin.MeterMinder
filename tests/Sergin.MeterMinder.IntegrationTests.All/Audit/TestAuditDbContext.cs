using MediatR;
using Microsoft.EntityFrameworkCore;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Events;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;

namespace Sergin.MeterMinder.IntegrationTests.All.Audit;

/// <summary>
/// A test-only module with one audited aggregate that can be renamed, because DeviceManagement has no
/// update slice yet and the Modified path needs one. Mapped into its own test_audit schema through the real
/// AddModuleDbContext, so the interceptors under test are the host's own. Its registry comes from an
/// explicit type list rather than an assembly scan: this test assembly also holds deliberately broken
/// configurations for the registry tests.
/// </summary>
internal interface ITestAuditDbContext : IDbContext;

internal interface ITestAuditUnitOfWork : IUnitOfWork;

internal sealed class TestAuditDbContext(DbContextOptions<TestAuditDbContext> options)
    : SerginDbContext(options), ITestAuditDbContext, ITestAuditUnitOfWork
{
    public const string Schema = "test_audit";

    public DbSet<AuditedThing> Things => Set<AuditedThing>();

    protected override AggregateFeatureRegistry AggregateFeatures =>
        AggregateFeatureRegistry.FromConfigurationTypes([typeof(AuditedThingAggregateConfiguration)]);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<AuditedThing>(thing =>
        {
            thing.ToTable("things");
            thing.HasKey(x => x.Id);
            thing.Property(x => x.Name);
        });

        base.OnModelCreating(modelBuilder);
    }
}

internal sealed class AuditedThing : AggregateRoot<Guid>
{
    private AuditedThing()
    {
    }

    public string Name { get; private set; } = string.Empty;

    /// <param name="spawnSibling">Raise an event whose handler adds a second thing on the same save.</param>
    public static AuditedThing Create(string name, bool spawnSibling = false)
    {
        AuditedThing thing = new() { Id = Guid.CreateVersion7(), Name = name };

        if (spawnSibling)
        {
            thing.Raise(new AuditedThingCreated(Guid.CreateVersion7(), DateTime.UtcNow, name));
        }

        return thing;
    }

    public void Rename(string name) => Name = name;
}

internal sealed class AuditedThingAggregateConfiguration : IAggregateConfiguration<AuditedThing>
{
    public void Configure(AggregateFeatureBuilder<AuditedThing> builder) => builder.Audited();
}

internal sealed record AuditedThingCreated(Guid Id, DateTime OccurredOnUtc, string Name) : IDomainEvent;

/// <summary>Adds a sibling on the originating save, so the test can check handler-added entities get stamped.</summary>
internal sealed class SiblingSpawningHandler(ITestAuditDbContext context)
    : INotificationHandler<DomainEventNotification<AuditedThingCreated>>
{
    public Task Handle(DomainEventNotification<AuditedThingCreated> notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);

        context.Set<AuditedThing>().Add(AuditedThing.Create($"{notification.Event.Name}-sibling"));
        return Task.CompletedTask;
    }
}

internal sealed record TestUserContext(UserId Id) : IUserContext
{
    public string UserName => "audit-test";

    public string FirstName => "Audit";

    public string LastName => "Test";

    public string Email => "audit-test@sergin.local";

    public HashSet<Permission> Permissions { get; } = [];
}
