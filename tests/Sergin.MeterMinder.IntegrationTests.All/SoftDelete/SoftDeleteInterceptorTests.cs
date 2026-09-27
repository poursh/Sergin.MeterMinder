using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Sergin.MeterMinder.IntegrationTests.All.Audit;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.SoftDelete;

/// <summary>
/// SoftDeleteInterceptor end to end against a test-only aggregate: a Remove becomes deleted_* stamps with
/// modified_* untouched; the row disappears from EF queries but stays in the table; the delete cascades to
/// child entities, loaded or not, with one instant and actor; owned rows survive; a child dropped from its
/// root's collection is soft-deleted on its own; a deleted row frees its unique key; and the CHECK constraint
/// refuses a half-set pair. Rows are read back in a fresh scope, so they come from Postgres.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class SoftDeleteInterceptorTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string ResetSchemaSql =
        "DROP SCHEMA IF EXISTS test_soft_delete CASCADE; CREATE SCHEMA test_soft_delete;";

    private static readonly string[] FilterName = [SoftDeleteColumns.QueryFilterName];

    private WebApplicationFactory<Program> softDeleteFactory = default!;

    public async Task InitializeAsync()
    {
        softDeleteFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices((context, services) =>
            services.AddModuleDbContext<TestSoftDeleteDbContext, ITestSoftDeleteDbContext, ITestSoftDeleteUnitOfWork>(
                context.Configuration.GetSection("Sergin"), TestSoftDeleteDbContext.Schema)));

        using IServiceScope scope = softDeleteFactory.Services.CreateScope();
        TestSoftDeleteDbContext context = scope.ServiceProvider.GetRequiredService<TestSoftDeleteDbContext>();

        await context.Database.ExecuteSqlRawAsync(ResetSchemaSql);
        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }

    public async Task DisposeAsync()
    {
        await softDeleteFactory.DisposeAsync();
    }

    [Fact]
    public async Task Remove_StampsDeleted_AndLeavesModifiedNull()
    {
        var creator = Guid.CreateVersion7();
        var deleter = Guid.CreateVersion7();
        Guid crateId = await SaveNewCrateAsync(creator, Crate.Create(UniqueCode()));

        DateTime before = DateTime.UtcNow;
        await RemoveCrateAsync(deleter, crateId);
        DateTime after = DateTime.UtcNow;

        DeletedRow row = await ReadCrateAsync(crateId);

        Assert.Equal(deleter, row.DeletedBy);
        AuditStampTests.AssertWithin(before, after, row.DeletedAtUtc!.Value);
        Assert.Null(row.ModifiedAtUtc);
        Assert.Null(row.ModifiedBy);
    }

    [Fact]
    public async Task DeletedRow_IsHiddenFromEfQueries_ButStaysInTheTable()
    {
        Guid crateId = await SaveNewCrateAsync(Guid.CreateVersion7(), Crate.Create(UniqueCode()));
        await RemoveCrateAsync(Guid.CreateVersion7(), crateId);

        using IServiceScope scope = softDeleteFactory.Services.CreateScope();
        TestSoftDeleteDbContext context = scope.ServiceProvider.GetRequiredService<TestSoftDeleteDbContext>();

        Assert.Null(await context.Crates.FindAsync(crateId));
        Assert.False(await context.Crates.AnyAsync(crate => crate.Id == crateId));
        Assert.True(await context.Crates.IgnoreQueryFilters(FilterName).AnyAsync(crate => crate.Id == crateId));
    }

    [Fact]
    public async Task Remove_CascadesToChildren_ThatWereNotLoaded()
    {
        var deleter = Guid.CreateVersion7();
        Guid crateId = await SaveNewCrateAsync(Guid.CreateVersion7(), Crate.Create(UniqueCode(), slotCount: 2));

        // FindAsync loads no navigation: the interceptor has to load the slots itself.
        await RemoveCrateAsync(deleter, crateId);

        DeletedRow crate = await ReadCrateAsync(crateId);
        IReadOnlyCollection<DeletedRow> slots = await ReadSlotsAsync(crateId);

        Assert.Equal(2, slots.Count);
        Assert.All(slots, slot =>
        {
            Assert.Equal(deleter, slot.DeletedBy);
            Assert.Equal(crate.DeletedAtUtc, slot.DeletedAtUtc);
        });
    }

    [Fact]
    public async Task Remove_LeavesOwnedRowsInPlace()
    {
        Guid crateId = await SaveNewCrateAsync(Guid.CreateVersion7(), Crate.Create(UniqueCode(), 0, "red", "blue"));

        using (IServiceScope scope = ScopeAs(Guid.CreateVersion7()))
        {
            TestSoftDeleteDbContext context = scope.ServiceProvider.GetRequiredService<TestSoftDeleteDbContext>();

            // SingleAsync loads the owned tags with their owner, so EF cascades the delete to them in the tracker.
            Crate crate = await context.Crates.SingleAsync(x => x.Id == crateId);
            Assert.Equal(2, crate.Tags.Count);

            context.Crates.Remove(crate);
            await context.SaveChangesAsync();
        }

        using IServiceScope readScope = softDeleteFactory.Services.CreateScope();
        TestSoftDeleteDbContext readContext = readScope.ServiceProvider.GetRequiredService<TestSoftDeleteDbContext>();

        Crate deleted = await readContext.Crates.IgnoreQueryFilters(FilterName).SingleAsync(x => x.Id == crateId);
        Assert.Equal(["blue", "red"], deleted.Tags.Select(tag => tag.Value).Order());
    }

    [Fact]
    public async Task ChildDroppedFromItsCollection_IsSoftDeleted_AndTheRootIsNot()
    {
        var actor = Guid.CreateVersion7();
        Guid crateId = await SaveNewCrateAsync(Guid.CreateVersion7(), Crate.Create(UniqueCode(), slotCount: 2));

        using (IServiceScope scope = ScopeAs(actor))
        {
            TestSoftDeleteDbContext context = scope.ServiceProvider.GetRequiredService<TestSoftDeleteDbContext>();
            Crate crate = await context.Crates.Include(x => x.Slots).SingleAsync(x => x.Id == crateId);

            crate.RemoveSlot(crate.Slots.OrderBy(slot => slot.Name).First());
            await context.SaveChangesAsync();
        }

        DeletedRow root = await ReadCrateAsync(crateId);
        DeletedRow[] slots = [.. (await ReadSlotsAsync(crateId)).OrderBy(slot => slot.DeletedAtUtc is null)];

        Assert.Null(root.DeletedAtUtc);
        Assert.Equal(actor, slots[0].DeletedBy);
        Assert.Null(slots[1].DeletedAtUtc);

        using IServiceScope readScope = softDeleteFactory.Services.CreateScope();
        TestSoftDeleteDbContext readContext = readScope.ServiceProvider.GetRequiredService<TestSoftDeleteDbContext>();
        Crate reloaded = await readContext.Crates.Include(x => x.Slots).SingleAsync(x => x.Id == crateId);

        Assert.Single(reloaded.Slots);
    }

    [Fact]
    public async Task DeletedRow_FreesItsUniqueKey()
    {
        string code = UniqueCode();
        Guid first = await SaveNewCrateAsync(Guid.CreateVersion7(), Crate.Create(code));
        await RemoveCrateAsync(Guid.CreateVersion7(), first);

        Guid second = await SaveNewCrateAsync(Guid.CreateVersion7(), Crate.Create(code));

        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task HalfSetPair_IsRefusedByTheCheckConstraint()
    {
        Guid crateId = await SaveNewCrateAsync(Guid.CreateVersion7(), Crate.Create(UniqueCode()));

        using IServiceScope scope = softDeleteFactory.Services.CreateScope();
        TestSoftDeleteDbContext context = scope.ServiceProvider.GetRequiredService<TestSoftDeleteDbContext>();

        PostgresException error = await Assert.ThrowsAsync<PostgresException>(() =>
            context.Database.ExecuteSqlAsync(
                $"UPDATE test_soft_delete.crates SET deleted_at_utc = now() WHERE id = {crateId}"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
    }

    private static string UniqueCode() => $"crate-{Guid.CreateVersion7()}";

    private IServiceScope ScopeAs(Guid userId)
    {
        IServiceScope scope = softDeleteFactory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = new TestUserContext(new UserId(userId));
        return scope;
    }

    private async Task<Guid> SaveNewCrateAsync(Guid actor, Crate crate)
    {
        using IServiceScope scope = ScopeAs(actor);
        TestSoftDeleteDbContext context = scope.ServiceProvider.GetRequiredService<TestSoftDeleteDbContext>();

        context.Crates.Add(crate);
        await context.SaveChangesAsync();

        return crate.Id;
    }

    private async Task RemoveCrateAsync(Guid actor, Guid crateId)
    {
        using IServiceScope scope = ScopeAs(actor);
        TestSoftDeleteDbContext context = scope.ServiceProvider.GetRequiredService<TestSoftDeleteDbContext>();

        Crate crate = Assert.IsType<Crate>(await context.Crates.FindAsync(crateId));
        context.Crates.Remove(crate);
        await context.SaveChangesAsync();
    }

    private async Task<DeletedRow> ReadCrateAsync(Guid crateId)
    {
        using IServiceScope scope = softDeleteFactory.Services.CreateScope();
        TestSoftDeleteDbContext context = scope.ServiceProvider.GetRequiredService<TestSoftDeleteDbContext>();

        return await context.Crates
            .IgnoreQueryFilters(FilterName)
            .Where(crate => crate.Id == crateId)
            .Select(crate => new DeletedRow(
                EF.Property<DateTime?>(crate, SoftDeleteColumns.DeletedAtUtc),
                EF.Property<Guid?>(crate, SoftDeleteColumns.DeletedBy),
                EF.Property<DateTime?>(crate, AuditColumns.ModifiedAtUtc),
                EF.Property<Guid?>(crate, AuditColumns.ModifiedBy)))
            .SingleAsync();
    }

    private async Task<IReadOnlyCollection<DeletedRow>> ReadSlotsAsync(Guid crateId)
    {
        using IServiceScope scope = softDeleteFactory.Services.CreateScope();
        TestSoftDeleteDbContext context = scope.ServiceProvider.GetRequiredService<TestSoftDeleteDbContext>();

        return await context.Set<Slot>()
            .IgnoreQueryFilters(FilterName)
            .Where(slot => slot.CrateId == crateId)
            .Select(slot => new DeletedRow(
                EF.Property<DateTime?>(slot, SoftDeleteColumns.DeletedAtUtc),
                EF.Property<Guid?>(slot, SoftDeleteColumns.DeletedBy),
                EF.Property<DateTime?>(slot, AuditColumns.ModifiedAtUtc),
                EF.Property<Guid?>(slot, AuditColumns.ModifiedBy)))
            .ToListAsync();
    }

    private sealed record DeletedRow(DateTime? DeletedAtUtc, Guid? DeletedBy, DateTime? ModifiedAtUtc, Guid? ModifiedBy);
}
