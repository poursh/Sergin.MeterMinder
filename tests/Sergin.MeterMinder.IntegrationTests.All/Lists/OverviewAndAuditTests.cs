using ErrorOr;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetAudit;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetOverview;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetAudit;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetOverview;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.IntegrationTests.All.Concurrency;
using Sergin.SharedKernel.Application;
using Sergin.SharedKernel.Application.Aggregates;
using Sergin.SharedKernel.Application.Commands.Queries;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;
using MediatR;

namespace Sergin.MeterMinder.IntegrationTests.All.Lists;

/// <summary>
/// The figures and stamps the aggregate pages show beside their body: the device overview, a manufacturer's
/// overview and its models' device counts, the information dialog's audit stamps, and the user names those stamps resolve to.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class OverviewAndAuditTests(SerginWebApiFactory<Program> factory)
{
    // The host's configured Sergin:DevUser, which the dispatcher acts as.
    private static readonly Guid DevUserId = Guid.Parse("01920000-0000-7000-8000-000000000001");

    [Fact]
    public async Task DeviceOverview_CountsANewDevice_AsAddedAndInUse()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();

        DeviceOverview before = await SendAsync(dispatcher, new GetDeviceOverviewQueryCommand());
        DeviceModelInternalId model = await CatalogSeed.ModelAsync(
            dispatcher, await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}"), $"model-{marker}");
        await CatalogSeed.DeviceAsync(dispatcher, $"dev-{marker}", model);
        DeviceOverview after = await SendAsync(dispatcher, new GetDeviceOverviewQueryCommand());

        Assert.Equal(before.Devices + 1, after.Devices);
        Assert.Equal(before.AddedLast30Days + 1, after.AddedLast30Days);
        Assert.Equal(before.ModelsInUse + 1, after.ModelsInUse);
        Assert.Equal(before.ModelsTotal + 1, after.ModelsTotal);
        Assert.Equal(before.Manufacturers + 1, after.Manufacturers);
    }

    [Fact]
    public async Task ManufacturerOverview_AndModelList_CountDevicesPerModel()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();

        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId used = await CatalogSeed.ModelAsync(dispatcher, manufacturer, "used");
        await CatalogSeed.ModelAsync(dispatcher, manufacturer, "unused");
        await CatalogSeed.DeviceAsync(dispatcher, $"{marker}-1", used);
        await CatalogSeed.DeviceAsync(dispatcher, $"{marker}-2", used);

        ManufacturerOverview overview = await SendAsync(dispatcher, new GetManufacturerOverviewQueryCommand(manufacturer.Value));
        ListQueryResponse<GetDeviceModelListItem> models =
            await SendAsync(dispatcher, new GetDeviceModelListQueryCommand(manufacturer, Paggination.Create(10, 1)));

        Assert.Equal(new ManufacturerOverview(2, 2), overview);
        Assert.Equal(2, Assert.Single(models.Data, m => m.Name == "used").DeviceCount);
        Assert.Equal(0, Assert.Single(models.Data, m => m.Name == "unused").DeviceCount);
    }

    [Fact]
    public async Task ModelList_SearchesAndSortsByName_AndTheTotalCountsOnlyMatches()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{CatalogSeed.Marker()}");
        await CatalogSeed.ModelAsync(dispatcher, manufacturer, "Beta 100");
        await CatalogSeed.ModelAsync(dispatcher, manufacturer, "Alpha 100");
        await CatalogSeed.ModelAsync(dispatcher, manufacturer, "Gamma 200");

        ListQueryResponse<GetDeviceModelListItem> found = await SendAsync(dispatcher, new GetDeviceModelListQueryCommand(
            manufacturer, Paggination.Create(10, 1), new Term("100"),
            sorting: Sorting.Create($$"""[{"id":"{{DeviceModelListFields.Name}}","desc":true}]""")));

        Assert.Equal(2, found.Total);
        Assert.Equal(["Beta 100", "Alpha 100"], found.Data.Select(m => m.Name));
    }

    [Fact]
    public async Task ModelList_RefusesAnUndeclaredSort()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{CatalogSeed.Marker()}");

        ErrorOr<ListQueryResponse<GetDeviceModelListItem>> result = await dispatcher.SendAsync(new GetDeviceModelListQueryCommand(
            manufacturer, Paggination.Create(10, 1), sorting: Sorting.Create("""[{"id":"deviceCount","desc":false}]""")));

        Assert.Equal("Sorting", Assert.Single(result.Errors).Code);
    }

    [Fact]
    public async Task ManufacturerOverview_OfAnUnknownManufacturer_IsNotFound()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<ManufacturerOverview> result = await dispatcher.SendAsync(new GetManufacturerOverviewQueryCommand(Guid.CreateVersion7()));

        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task DeviceAudit_IsStampedByTheDispatcherUser_AndModifiedOnlyAfterAnUpdate()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        Guid devUser = DevUserId;
        string marker = CatalogSeed.Marker();

        DeviceModelInternalId model = await CatalogSeed.ModelAsync(
            dispatcher, await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}"), $"model-{marker}");
        Guid device = await CatalogSeed.DeviceAsync(dispatcher, $"dev-{marker}", model);

        AuditInfo created = await SendAsync(dispatcher, new GetDeviceAuditQueryCommand(device));

        Assert.Equal(devUser, created.CreatedBy);
        Assert.Null(created.ModifiedAtUtc);
        Assert.Null(created.ModifiedBy);

        ErrorOr<UpdateDeviceCommandResponse> updated = await dispatcher.SendAtDeviceVersionAsync(
            device, new UpdateDeviceCommand(device, new DeviceId($"renamed-{marker}"), model));
        Assert.False(updated.IsError, updated.IsError ? updated.FirstError.Description : string.Empty);

        AuditInfo modified = await SendAsync(dispatcher, new GetDeviceAuditQueryCommand(device));

        Assert.Equal(created.CreatedAtUtc, modified.CreatedAtUtc);
        Assert.Equal(devUser, modified.ModifiedBy);
        Assert.NotNull(modified.ModifiedAtUtc);
    }

    [Fact]
    public async Task Audit_NamesTheDeviceScope_AndNoScopeForAManufacturer()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();
        string marker = CatalogSeed.Marker();

        ManufacturerId manufacturer = await CatalogSeed.ManufacturerAsync(dispatcher, $"maker-{marker}");
        DeviceModelInternalId model = await CatalogSeed.ModelAsync(dispatcher, manufacturer, $"model-{marker}");
        Guid device = await CatalogSeed.DeviceAsync(dispatcher, $"dev-{marker}", model);

        AuditInfo deviceAudit = await SendAsync(dispatcher, new GetDeviceAuditQueryCommand(device));
        AuditInfo manufacturerAudit = await SendAsync(dispatcher, new GetManufacturerAuditQueryCommand(manufacturer.Value));

        // The dispatcher works in the root scope unless told otherwise, so that is where the device landed.
        Assert.Equal(AuthorizationScope.RootId, deviceAudit.ScopeId);
        Assert.Null(manufacturerAudit.ScopeId);
    }

    [Fact]
    public async Task Audit_OfAnUnknownRecord_IsNotFound()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<AuditInfo> device = await dispatcher.SendAsync(new GetDeviceAuditQueryCommand(Guid.CreateVersion7()));
        ErrorOr<AuditInfo> manufacturer = await dispatcher.SendAsync(new GetManufacturerAuditQueryCommand(Guid.CreateVersion7()));

        Assert.Equal(ErrorType.NotFound, device.FirstError.Type);
        Assert.Equal(ErrorType.NotFound, manufacturer.FirstError.Type);
    }

    [Fact]
    public async Task UserDirectory_NamesTheDevUser_AndLeavesAnUnknownIdOut()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IUserDirectory directory = scope.ServiceProvider.GetRequiredService<IUserDirectory>();
        var unknown = Guid.CreateVersion7();

        IReadOnlyDictionary<Guid, string> names = await directory.GetDisplayNamesAsync([DevUserId, unknown]);

        Assert.True(names.TryGetValue(DevUserId, out string? name));
        Assert.False(string.IsNullOrWhiteSpace(name));
        Assert.False(names.ContainsKey(unknown));
    }

    private static async Task<T> SendAsync<T>(ISerginDispatcher dispatcher, IRequest<ErrorOr<T>> request)
    {
        ErrorOr<T> result = await dispatcher.SendAsync(request);

        Assert.False(result.IsError, result.IsError ? result.FirstError.Description : string.Empty);

        return result.Value;
    }
}
