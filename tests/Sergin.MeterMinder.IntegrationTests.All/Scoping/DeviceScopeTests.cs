using ErrorOr;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Events.Integration;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Aggregates;
using Sergin.SharedKernel.Infrastructure.Data.EFCore.Outbox;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using Sergin.UserAccess.Application.Contracts.Scopes.Commands.Move;

namespace Sergin.MeterMinder.IntegrationTests.All.Scoping;

/// <summary>
/// Device is Scoped(): a new device takes the caller's current scope, and every read, through EF or raw SQL, sees
/// only devices inside the current scope's subtree that the caller's grants reach. Built on the acceptance tree, one
/// device per scope, every send through ISerginDispatcher from a "circuit" scope whose current scope the test sets.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class DeviceScopeTests(SerginWebApiFactory<Program> factory)
{
    private static readonly string[] AllFilters = [ScopeColumns.QueryFilterName, SoftDeleteColumns.QueryFilterName];

    [Fact]
    public async Task CreateDevice_IsStampedWithTheCurrentScope_AndAnUpdateKeepsIt()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        DeviceModelInternalId model = await CreateModelAsync();

        Guid device = await CreateDeviceAsync(model, tree.Feeder1);
        Guid atRoot = await CreateDeviceAsync(model, null);

        Assert.Equal((tree.Feeder1, await PathOfAsync(tree.Feeder1)), await StoredScopeAsync(device));
        Assert.Equal((AuthorizationScope.RootId, AuthorizationScope.RootPath), await StoredScopeAsync(atRoot));

        ErrorOr<UpdateDeviceCommandResponse> updated = await SendAtVersionAsync(
            device, new UpdateDeviceCommand(device, new DeviceId($"renamed-{Guid.CreateVersion7()}"), model), tree.North);

        Assert.False(updated.IsError, updated.IsError ? updated.FirstError.Description : string.Empty);
        Assert.Equal(tree.Feeder1, (await StoredScopeAsync(device)).ScopeId);
    }

    [Fact]
    public async Task Reads_SeeTheCurrentSubtree_WithinTheCallersReach()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        DeviceModelInternalId model = await CreateModelAsync();
        Guid north = await CreateDeviceAsync(model, tree.North);
        Guid feeder1 = await CreateDeviceAsync(model, tree.Feeder1);
        Guid south = await CreateDeviceAsync(model, tree.South);

        TestCaller northSubtree = await tree.CreateGrantedCallerAsync(tree.North, RbacScenario.Read);

        Assert.Equal(2, await CountAsync(northSubtree, tree.North));
        Assert.Equal(1, await CountAsync(northSubtree, tree.Feeder1));
        Assert.False((await GetAsync(northSubtree, feeder1, tree.North)).IsError);
        Assert.Equal(ErrorType.NotFound, (await GetAsync(northSubtree, south, tree.North)).FirstError.Type);

        // The pipeline refuses before any read where the caller holds nothing.
        Assert.Equal(ErrorType.Forbidden, (await GetAsync(northSubtree, south, tree.South)).FirstError.Type);

        // At the company the subtree holds all three, and the reach still stops at North.
        TestCaller companyScopeOnly = await GrantedOnItsScopeOnlyAsync(tree, tree.Company);
        Guid company = await CreateDeviceAsync(model, tree.Company);
        Assert.Equal(1, await CountAsync(companyScopeOnly, tree.Company));
        Assert.False((await GetAsync(companyScopeOnly, company, tree.Company)).IsError);
        Assert.Equal(ErrorType.NotFound, (await GetAsync(companyScopeOnly, north, tree.Company)).FirstError.Type);

        // A system admin reaches everything below the current scope.
        Assert.Equal(4, await CountAsync(new TestCaller { IsSystemAdmin = true }, tree.Company));
    }

    [Fact]
    public async Task ScopeFilter_IsAppliedPerQuery_ThroughEf()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        DeviceModelInternalId model = await CreateModelAsync();
        Guid[] ids =
        [
            await CreateDeviceAsync(model, tree.North),
            await CreateDeviceAsync(model, tree.Feeder1),
            await CreateDeviceAsync(model, tree.South),
        ];

        string company = (await PathOfAsync(tree.Company))!;
        string north = (await PathOfAsync(tree.North))!;
        var northSubtree = ScopeReach.Of([new ScopeReachEntry(tree.North, north, IncludeSubtree: true)]);
        var southOnly = ScopeReach.Of([new ScopeReachEntry(tree.South, (await PathOfAsync(tree.South))!, IncludeSubtree: false)]);

        using IServiceScope first = factory.Services.CreateScope();
        using IServiceScope second = factory.Services.CreateScope();
        ScopeFilter firstFilter = first.ServiceProvider.GetRequiredService<ScopeFilter>();
        ScopeFilter secondFilter = second.ServiceProvider.GetRequiredService<ScopeFilter>();

        using (firstFilter.Apply(company, northSubtree))
        using (secondFilter.Apply(company, southOnly))
        {
            Assert.Equal(2, await CountThroughEfAsync(first, ids));
            Assert.Equal(1, await CountThroughEfAsync(second, ids));

            // A subtree outside the current scope reaches nothing there.
            using (firstFilter.Apply(north, southOnly))
            {
                Assert.Equal(0, await CountThroughEfAsync(first, ids));
            }

            Assert.Equal(2, await CountThroughEfAsync(first, ids));
        }

        // Outside any request a non-admin caller sees no device at all.
        Assert.Equal(0, await CountThroughEfAsync(first, ids));
    }

    [Fact]
    public async Task UpdateDevice_OutsideTheReach_IsNotFound_AndChangesNothing()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        DeviceModelInternalId model = await CreateModelAsync();
        Guid south = await CreateDeviceAsync(model, tree.South);
        TestCaller northUpdater = await tree.CreateGrantedCallerAsync(tree.North, RbacScenario.Read, RbacScenario.Update);

        ErrorOr<UpdateDeviceCommandResponse> updated = await SendAsync(
            new UpdateDeviceCommand(south, new DeviceId($"hijacked-{Guid.CreateVersion7()}"), model), tree.North, northUpdater, RowVersion.Create());

        Assert.True(updated.IsError);
        Assert.Equal(tree.South, (await StoredScopeAsync(south)).ScopeId);
    }

    [Fact]
    public async Task CreateDevice_WithAnIdTakenInAnotherScope_IsAValidationError()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        DeviceModelInternalId model = await CreateModelAsync();
        DeviceId deviceId = new($"shared-{Guid.CreateVersion7()}");
        Assert.False((await SendAsync(new CreateDeviceCommand(deviceId, model), tree.South)).IsError);

        TestCaller northUpdater = await tree.CreateGrantedCallerAsync(tree.North, RbacScenario.Read, RbacScenario.Update);
        ErrorOr<CreateDeviceCommandResponse> duplicate = await SendAsync(new CreateDeviceCommand(deviceId, model), tree.North, northUpdater);

        Assert.Equal(ErrorType.Validation, duplicate.FirstError.Type);
    }

    [Fact]
    public async Task CreateDevice_NeedsUpdateInTheCurrentScope()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        DeviceModelInternalId model = await CreateModelAsync();
        TestCaller northUpdater = await tree.CreateGrantedCallerAsync(tree.North, RbacScenario.Update);

        CreateDeviceCommand create() => new(new DeviceId($"device-{Guid.CreateVersion7()}"), model);

        Assert.False((await SendAsync(create(), tree.Feeder2, northUpdater)).IsError);
        Assert.Equal(ErrorType.Forbidden, (await SendAsync(create(), tree.South, northUpdater)).FirstError.Type);
        Assert.Equal(ErrorType.Forbidden, (await SendAsync(create(), null, northUpdater)).FirstError.Type);
    }

    [Fact]
    public async Task ScopeMove_RewritesTheStoredPath_AndARepeatedEventChangesNothing()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        DeviceModelInternalId model = await CreateModelAsync();
        Guid device = await CreateDeviceAsync(model, tree.Feeder1);

        Assert.False((await SendAsync(new MoveScopeCommand(tree.Feeder1, tree.South), null)).IsError);
        string moved = (await PathOfAsync(tree.Feeder1))!;
        Assert.StartsWith((await PathOfAsync(tree.South))!, moved, StringComparison.Ordinal);

        await RelayUntilAsync(async () => (await StoredScopeAsync(device)).ScopePath == moved);

        // The handler re-reads the current path, so a redelivered event, or one from an earlier move, is harmless.
        await PublishMovedAsync([tree.Feeder1]);
        Assert.Equal((tree.Feeder1, moved), await StoredScopeAsync(device));

        TestCaller southReader = await tree.CreateGrantedCallerAsync(tree.South, RbacScenario.Read);
        TestCaller northReader = await tree.CreateGrantedCallerAsync(tree.North, RbacScenario.Read);
        Assert.False((await GetAsync(southReader, device, tree.South)).IsError);
        Assert.Equal(ErrorType.NotFound, (await GetAsync(northReader, device, tree.North)).FirstError.Type);
    }

    private async Task<TestCaller> GrantedOnItsScopeOnlyAsync(RbacScenario tree, Guid scopeId)
    {
        Guid role = await tree.CreateRoleAsync(scopeId, $"own-{Guid.NewGuid():N}"[..20], inheritable: true, RbacScenario.Read);
        Guid user = await tree.CreateUserAsync("own-scope");
        Assert.False((await tree.AssignAsync(user, role, scopeId, includeSubtree: false)).IsError);

        return new TestCaller { Id = new Sergin.SharedKernel.Domain.Users.UserId(user) };
    }

    private async Task<int> CountAsync(IUserContext caller, Guid scopeId)
    {
        ErrorOr<ListQueryResponse<GetDeviceListItem>> listed =
            await SendAsync(new GetDeviceListQueryCommand(Paggination.Create(100, 1)), scopeId, caller);

        Assert.False(listed.IsError, listed.IsError ? listed.FirstError.Description : string.Empty);
        Assert.Equal(listed.Value.Total, listed.Value.Data.Count);

        return listed.Value.Total;
    }

    private Task<ErrorOr<DeviceQueryResponse>> GetAsync(IUserContext caller, Guid deviceId, Guid scopeId) =>
        SendAsync(new GetDeviceByIdQueryCommand(deviceId), scopeId, caller);

    private async Task<DeviceModelInternalId> CreateModelAsync()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<CreateManufacturerCommandResponse> manufacturer = await dispatcher.SendAsync(
            new CreateManufacturerCommand(new ManufacturerName($"maker-{Guid.CreateVersion7()}"), Address: null));
        Assert.False(manufacturer.IsError, manufacturer.IsError ? manufacturer.FirstError.Description : string.Empty);

        ErrorOr<Versioned<ManufacturerQueryResponse>> loaded =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturer.Value.Id));
        ErrorOr<Versioned<AddDeviceModelCommandResponse>> added = await dispatcher.SendVersionedAsync(
            new AddDeviceModelCommand(new ManufacturerId(manufacturer.Value.Id), new DeviceModelName($"model-{Guid.CreateVersion7()}")),
            loaded.Value.Version);
        Assert.False(added.IsError, added.IsError ? added.FirstError.Description : string.Empty);

        return new DeviceModelInternalId(added.Value.Value.Id);
    }

    /// <summary>Created by the development user, a root administrator, working in <paramref name="scopeId"/>.</summary>
    private async Task<Guid> CreateDeviceAsync(DeviceModelInternalId model, Guid? scopeId)
    {
        ErrorOr<CreateDeviceCommandResponse> created =
            await SendAsync(new CreateDeviceCommand(new DeviceId($"device-{Guid.CreateVersion7()}"), model), scopeId);

        Assert.False(created.IsError, created.IsError ? created.FirstError.Description : string.Empty);

        return created.Value.Id;
    }

    private async Task<ErrorOr<TResponse>> SendAtVersionAsync<TResponse>(Guid deviceId, IRequest<ErrorOr<TResponse>> command, Guid scopeId)
    {
        ErrorOr<Versioned<DeviceQueryResponse>> loaded;

        using (IServiceScope circuit = Circuit(scopeId, caller: null))
        {
            loaded = await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>()
                .SendVersionedAsync(new GetDeviceByIdQueryCommand(deviceId));
        }

        Assert.False(loaded.IsError, loaded.IsError ? loaded.FirstError.Description : string.Empty);

        return await SendAsync(command, scopeId, caller: null, loaded.Value.Version);
    }

    /// <summary>Sends from a scope standing in for a Blazor circuit, with its current scope and caller set when given.</summary>
    private async Task<ErrorOr<TResponse>> SendAsync<TResponse>(
        IRequest<ErrorOr<TResponse>> request, Guid? scopeId, IUserContext? caller = null, RowVersion? version = null)
    {
        using IServiceScope circuit = Circuit(scopeId, caller);
        ISerginDispatcher dispatcher = circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        if (version is null)
        {
            return await dispatcher.SendAsync(request);
        }

        return (await dispatcher.SendVersionedAsync(request, version)).Then(sent => sent.Value);
    }

    private IServiceScope Circuit(Guid? scopeId, IUserContext? caller)
    {
        IServiceScope circuit = factory.Services.CreateScope();

        if (caller is not null)
        {
            circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        }

        if (scopeId is { } id)
        {
            circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(id);
        }

        return circuit;
    }

    private async Task<string?> PathOfAsync(Guid scopeId)
    {
        using IServiceScope scope = factory.Services.CreateScope();

        return await scope.ServiceProvider.GetRequiredService<IScopePathResolver>().GetPathAsync(scopeId);
    }

    private async Task<(Guid ScopeId, string ScopePath)> StoredScopeAsync(Guid deviceId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IDeviceManagementDbContext context = scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>();
        DeviceIntenralId id = new(deviceId);

        var stored = await context.Set<Device>()
            .IgnoreQueryFilters(AllFilters)
            .Where(device => device.Id == id)
            .Select(device => new
            {
                ScopeId = EF.Property<Guid>(device, ScopeColumns.ScopeId),
                ScopePath = EF.Property<LTree>(device, ScopeColumns.ScopePath),
            })
            .SingleAsync();

        return (stored.ScopeId, stored.ScopePath.ToString());
    }

    private static async Task<int> CountThroughEfAsync(IServiceScope scope, Guid[] deviceIds)
    {
        IDeviceManagementDbContext context = scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>();

        IReadOnlyCollection<DeviceIntenralId> visible = [.. await context.Set<Device>().AsNoTracking().Select(device => device.Id).ToListAsync()];

        return deviceIds.Count(id => visible.Contains(new DeviceIntenralId(id)));
    }

    /// <summary>Runs UserAccess's outbox until <paramref name="done"/> holds; the background relay may get there first.</summary>
    private async Task RelayUntilAsync(Func<Task<bool>> done)
    {
        IOutboxRelaySource userAccess = factory.Services.GetServices<IOutboxRelaySource>().Single(source => source.Schema == "ua");

        for (int attempt = 0; attempt < 40; attempt++)
        {
            if (await done())
            {
                return;
            }

            await userAccess.RelayOnceAsync(CancellationToken.None);
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }

        Assert.Fail("The scope move never reached the device's stored path.");
    }

    private async Task PublishMovedAsync(IReadOnlyCollection<Guid> scopeIds)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = new TestCaller { IsSystemAdmin = true };

        ScopesMovedIntegrationEvent moved = new(Guid.CreateVersion7(), DateTime.UtcNow, scopeIds);

        await scope.ServiceProvider.GetRequiredService<IPublisher>()
            .Publish(new IntegrationEventNotification<ScopesMovedIntegrationEvent>(Guid.CreateVersion7(), "test", moved));
    }
}
