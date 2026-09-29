using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.IntegrationTests.All.Audit;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Events;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.Users;
using Sergin.SharedKernel.Infrastructure.Data.EFCore;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.IntegrationTests;

namespace Sergin.MeterMinder.IntegrationTests.All.Concurrency;

/// <summary>
/// The two row-version interceptors against the test-only Pallet aggregate, with the host's real interceptor
/// chain: every save that changes the aggregate writes a new version and publishes it; a stale expected
/// version refuses the save and persists nothing; a change to a child alone still moves the root's version;
/// a soft delete is checked like any update. Each step runs in its own scope, as a request would.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class RowVersionInterceptorTests(SerginWebApiFactory<Program> factory) : IAsyncLifetime
{
    private const string ResetSchemaSql =
        "DROP SCHEMA IF EXISTS test_versioned CASCADE; CREATE SCHEMA test_versioned;";

    private static readonly string[] FilterName = [SoftDeleteColumns.QueryFilterName];

    private WebApplicationFactory<Program> versionedFactory = default!;

    public async Task InitializeAsync()
    {
        versionedFactory = factory.WithWebHostBuilder(builder => builder.ConfigureServices((context, services) =>
        {
            services.AddModuleDbContext<TestVersionedDbContext, ITestVersionedDbContext, ITestVersionedUnitOfWork>(
                context.Configuration.GetSection("Sergin"), TestVersionedDbContext.Schema);
            services.AddTransient<INotificationHandler<DomainEventNotification<PalletNudged>>, RenameNeighbourHandler>();
        }));

        using IServiceScope scope = versionedFactory.Services.CreateScope();
        TestVersionedDbContext context = scope.ServiceProvider.GetRequiredService<TestVersionedDbContext>();

        await context.Database.ExecuteSqlRawAsync(ResetSchemaSql);
        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
    }

    public async Task DisposeAsync() => await versionedFactory.DisposeAsync();

    [Fact]
    public async Task Insert_SetsAVersion_AndPublishesIt()
    {
        (Guid id, RowVersion? published) = await SaveNewAsync(Pallet.Create(UniqueCode()));

        Assert.NotNull(published);
        Assert.Equal(published.Value, await ReadVersionAsync(id));
    }

    [Fact]
    public async Task Update_WithTheCurrentVersion_WritesANewOne()
    {
        (Guid id, RowVersion? first) = await SaveNewAsync(Pallet.Create(UniqueCode()));

        RowVersion? second = await ChangeAsync(id, first, pallet => pallet.Rename(UniqueCode()));

        Assert.NotNull(second);
        Assert.NotEqual(first!.Value, second.Value);
        Assert.Equal(second.Value, await ReadVersionAsync(id));
    }

    [Fact]
    public async Task ChildOnlyChange_MovesTheRootsVersion()
    {
        (Guid id, RowVersion? first) = await SaveNewAsync(Pallet.Create(UniqueCode()));

        RowVersion? second = await ChangeAsync(id, first, pallet => pallet.AddBay("bay-1"), includeBays: true);

        Assert.NotEqual(first!.Value, second!.Value);
        Assert.Equal(second.Value, await ReadVersionAsync(id));
    }

    [Fact]
    public async Task StaleVersion_RefusesTheSave_AndPersistsNothing()
    {
        string code = UniqueCode();
        (Guid id, RowVersion? first) = await SaveNewAsync(Pallet.Create(code));
        await ChangeAsync(id, first, pallet => pallet.AddBay("bay-1"), includeBays: true);

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() =>
            ChangeAsync(id, first, pallet => pallet.Rename("stale-rename")));

        using IServiceScope scope = versionedFactory.Services.CreateScope();
        TestVersionedDbContext context = scope.ServiceProvider.GetRequiredService<TestVersionedDbContext>();
        Assert.Equal(code, (await context.Pallets.SingleAsync(pallet => pallet.Id == id)).Code);
    }

    [Fact]
    public async Task SoftDelete_WithAStaleVersion_IsRefused()
    {
        (Guid id, RowVersion? first) = await SaveNewAsync(Pallet.Create(UniqueCode()));
        await ChangeAsync(id, first, pallet => pallet.Rename(UniqueCode()));

        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => RemoveAsync(id, first));

        using IServiceScope scope = versionedFactory.Services.CreateScope();
        TestVersionedDbContext context = scope.ServiceProvider.GetRequiredService<TestVersionedDbContext>();
        Assert.True(await context.Pallets.AnyAsync(pallet => pallet.Id == id));
    }

    [Fact]
    public async Task SoftDelete_WithTheCurrentVersion_MovesTheVersion()
    {
        (Guid id, RowVersion? first) = await SaveNewAsync(Pallet.Create(UniqueCode()));

        await RemoveAsync(id, first);

        Assert.NotEqual(first!.Value, await ReadVersionAsync(id));
    }

    [Fact]
    public async Task ExpectedVersion_WithNoChange_Succeeds()
    {
        (Guid id, RowVersion? first) = await SaveNewAsync(Pallet.Create(UniqueCode()));

        await ChangeAsync(id, RowVersion.Create(), _ => { });

        Assert.Equal(first!.Value, await ReadVersionAsync(id));
    }

    [Fact]
    public async Task ExpectedVersion_WithTwoRootsTouched_Throws()
    {
        (Guid a, RowVersion? versionA) = await SaveNewAsync(Pallet.Create(UniqueCode()));
        (Guid b, _) = await SaveNewAsync(Pallet.Create(UniqueCode()));

        using IServiceScope scope = ScopeWith(versionA);
        TestVersionedDbContext context = scope.ServiceProvider.GetRequiredService<TestVersionedDbContext>();
        foreach (Pallet pallet in await context.Pallets.Where(p => p.Id == a || p.Id == b).ToListAsync())
        {
            pallet.Rename(UniqueCode());
        }

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => context.SaveChangesAsync());
        Assert.Contains(nameof(Pallet), error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RootChangedByAnEventHandler_IsBumped_ButNotChecked()
    {
        (Guid a, RowVersion? versionA) = await SaveNewAsync(Pallet.Create(UniqueCode()));
        (Guid b, RowVersion? versionB) = await SaveNewAsync(Pallet.Create(UniqueCode()));

        RowVersion? published = await ChangeAsync(a, versionA, pallet => pallet.Nudge(b));

        Assert.Equal(published!.Value, await ReadVersionAsync(a));
        Assert.NotEqual(versionB!.Value, await ReadVersionAsync(b));
    }

    private static string UniqueCode() => $"pallet-{Guid.CreateVersion7()}";

    private IServiceScope ScopeWith(RowVersion? expected)
    {
        IServiceScope scope = versionedFactory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = new TestUserContext(new UserId(Guid.CreateVersion7()));
        scope.ServiceProvider.GetRequiredService<ConcurrencyContext>().Expected = expected;
        return scope;
    }

    private async Task<(Guid Id, RowVersion? Published)> SaveNewAsync(Pallet pallet)
    {
        using IServiceScope scope = ScopeWith(expected: null);
        TestVersionedDbContext context = scope.ServiceProvider.GetRequiredService<TestVersionedDbContext>();

        context.Pallets.Add(pallet);
        await context.SaveChangesAsync();

        return (pallet.Id, scope.ServiceProvider.GetRequiredService<ConcurrencyContext>().Current);
    }

    private async Task<RowVersion?> ChangeAsync(Guid id, RowVersion? expected, Action<Pallet> change, bool includeBays = false)
    {
        using IServiceScope scope = ScopeWith(expected);
        TestVersionedDbContext context = scope.ServiceProvider.GetRequiredService<TestVersionedDbContext>();

        IQueryable<Pallet> pallets = includeBays ? context.Pallets.Include(p => p.Bays) : context.Pallets;
        change(await pallets.SingleAsync(p => p.Id == id));
        await context.SaveChangesAsync();

        return scope.ServiceProvider.GetRequiredService<ConcurrencyContext>().Current;
    }

    private async Task RemoveAsync(Guid id, RowVersion? expected)
    {
        using IServiceScope scope = ScopeWith(expected);
        TestVersionedDbContext context = scope.ServiceProvider.GetRequiredService<TestVersionedDbContext>();

        context.Pallets.Remove(await context.Pallets.SingleAsync(p => p.Id == id));
        await context.SaveChangesAsync();
    }

    private async Task<Guid> ReadVersionAsync(Guid id)
    {
        using IServiceScope scope = versionedFactory.Services.CreateScope();
        TestVersionedDbContext context = scope.ServiceProvider.GetRequiredService<TestVersionedDbContext>();

        return await context.Pallets
            .IgnoreQueryFilters(FilterName)
            .Where(pallet => pallet.Id == id)
            .Select(pallet => EF.Property<Guid>(pallet, RowVersionColumns.RowVersion))
            .SingleAsync();
    }

    /// <summary>Renames the nudged pallet on the same save, without saving: a second root the handler never loaded itself.</summary>
    private sealed class RenameNeighbourHandler(TestVersionedDbContext context) : IDomainEventHandler<PalletNudged>
    {
        public async Task Handle(PalletNudged domainEvent, CancellationToken cancellationToken)
        {
            Pallet neighbour = await context.Pallets.SingleAsync(p => p.Id == domainEvent.NeighbourId, cancellationToken);
            neighbour.Rename($"nudged-{Guid.CreateVersion7()}");
        }
    }
}
