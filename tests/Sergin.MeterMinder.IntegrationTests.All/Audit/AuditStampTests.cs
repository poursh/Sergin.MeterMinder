using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Sergin.SharedKernel.Application.Events;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Audit;

/// <summary>
/// AuditStampInterceptor end to end: an insert stamps created_* and leaves modified_* NULL, an update stamps
/// modified_* and leaves created_* alone, an entity a domain-event handler adds on the same save is stamped,
/// and the actor is whatever IUserContext the scope resolves — here one seeded through UserContextAccessor,
/// the way the Blazor dispatcher and the outbox relay hand their identity into a scope. Values are read back
/// through EF.Property in a fresh scope, so they come from Postgres, not the change tracker.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class AuditStampTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string ResetSchemaSql = "DROP SCHEMA IF EXISTS test_audit CASCADE; CREATE SCHEMA test_audit;";

    private WebApplicationFactory<Program> auditFactory = default!;

    public async Task InitializeAsync()
    {
        auditFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices((context, services) =>
        {
            services.AddModuleDbContext<TestAuditDbContext, ITestAuditDbContext, ITestAuditUnitOfWork>(
                context.Configuration.GetSection("Sergin"), TestAuditDbContext.Schema);

            services.AddTransient<INotificationHandler<DomainEventNotification<AuditedThingCreated>>, SiblingSpawningHandler>();
        }));

        using IServiceScope scope = auditFactory.Services.CreateScope();
        TestAuditDbContext context = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();

        // Not EnsureCreatedAsync: it no-ops once the database holds any table. CreateTablesAsync builds this
        // model's tables, audit columns included, regardless.
        await context.Database.ExecuteSqlRawAsync(ResetSchemaSql);
        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }

    public async Task DisposeAsync()
    {
        await auditFactory.DisposeAsync();
    }

    [Fact]
    public async Task Insert_StampsCreated_AndLeavesModifiedNull()
    {
        var actor = Guid.CreateVersion7();
        DateTime before = DateTime.UtcNow;

        Guid thingId = await SaveNewThingAsync(actor, "created");

        DateTime after = DateTime.UtcNow;
        AuditRow row = await ReadThingAsync(thingId);

        Assert.Equal(actor, row.CreatedBy);
        AssertWithin(before, after, row.CreatedAtUtc);
        Assert.Null(row.ModifiedAtUtc);
        Assert.Null(row.ModifiedBy);
    }

    [Fact]
    public async Task Update_StampsModified_AndLeavesCreatedAlone()
    {
        var creator = Guid.CreateVersion7();
        var editor = Guid.CreateVersion7();

        Guid thingId = await SaveNewThingAsync(creator, "original");
        AuditRow afterCreate = await ReadThingAsync(thingId);

        DateTime before = DateTime.UtcNow;

        using (IServiceScope scope = ScopeAs(editor))
        {
            TestAuditDbContext context = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();
            AuditedThing thing = await context.Things.SingleAsync(x => x.Id == thingId);
            thing.Rename("renamed");
            await context.SaveChangesAsync();
        }

        DateTime after = DateTime.UtcNow;
        AuditRow afterUpdate = await ReadThingAsync(thingId);

        Assert.Equal(creator, afterUpdate.CreatedBy);
        Assert.Equal(afterCreate.CreatedAtUtc, afterUpdate.CreatedAtUtc);
        Assert.Equal(editor, afterUpdate.ModifiedBy);
        AssertWithin(before, after, afterUpdate.ModifiedAtUtc!.Value);
    }

    [Fact]
    public async Task EntityAddedByDomainEventHandler_IsStamped()
    {
        var actor = Guid.CreateVersion7();
        string name = $"parent-{Guid.CreateVersion7()}";

        using (IServiceScope scope = ScopeAs(actor))
        {
            TestAuditDbContext context = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();
            context.Things.Add(AuditedThing.Create(name, spawnSibling: true));
            await context.SaveChangesAsync();
        }

        using IServiceScope readScope = auditFactory.Services.CreateScope();
        TestAuditDbContext readContext = readScope.ServiceProvider.GetRequiredService<TestAuditDbContext>();

        Guid siblingCreatedBy = await readContext.Things
            .Where(thing => thing.Name == $"{name}-sibling")
            .Select(thing => EF.Property<Guid>(thing, AuditColumns.CreatedBy))
            .SingleAsync();

        Assert.Equal(actor, siblingCreatedBy);
    }

    // Postgres keeps microseconds and .NET ticks are 100 ns, so allow a millisecond either side.
    internal static void AssertWithin(DateTime before, DateTime after, DateTime actual) =>
        Assert.InRange(actual, before.AddMilliseconds(-1), after.AddMilliseconds(1));

    private IServiceScope ScopeAs(Guid userId)
    {
        IServiceScope scope = auditFactory.Services.CreateScope();

        // Seed before anything resolves the DbContext: the interceptor takes IUserContext when the context
        // is built, and the scoped IUserContext registration prefers a seeded value.
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = new TestUserContext(new UserId(userId));
        return scope;
    }

    private async Task<Guid> SaveNewThingAsync(Guid actor, string name)
    {
        using IServiceScope scope = ScopeAs(actor);
        TestAuditDbContext context = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();

        var thing = AuditedThing.Create(name);
        context.Things.Add(thing);
        await context.SaveChangesAsync();

        return thing.Id;
    }

    private async Task<AuditRow> ReadThingAsync(Guid thingId)
    {
        using IServiceScope scope = auditFactory.Services.CreateScope();
        TestAuditDbContext context = scope.ServiceProvider.GetRequiredService<TestAuditDbContext>();

        return await context.Things
            .Where(thing => thing.Id == thingId)
            .Select(thing => new AuditRow(
                EF.Property<DateTime>(thing, AuditColumns.CreatedAtUtc),
                EF.Property<Guid>(thing, AuditColumns.CreatedBy),
                EF.Property<DateTime?>(thing, AuditColumns.ModifiedAtUtc),
                EF.Property<Guid?>(thing, AuditColumns.ModifiedBy)))
            .SingleAsync();
    }

    internal sealed record AuditRow(DateTime CreatedAtUtc, Guid CreatedBy, DateTime? ModifiedAtUtc, Guid? ModifiedBy);
}
