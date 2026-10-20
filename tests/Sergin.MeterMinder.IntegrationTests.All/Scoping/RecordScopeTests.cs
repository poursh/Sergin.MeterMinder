using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers;
using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers.DeviceModels;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Concurrency;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Scoping;

/// <summary>
/// RecordScopeInterceptor: a save is checked again on the scope of every record it writes. A manufacturer belongs to
/// no scope, so its save asks nothing the pipeline did not: a regional holder may change it. A new device is checked on
/// the scope it is stamped with, even when the request was bound to an unscoped aggregate and never checked the
/// current scope.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class RecordScopeTests(SerginWebApiFactory<Program> factory)
{
    private static readonly Permission ManufacturersUpdate = ManufacturerPermissions.Update.Permission;

    [Fact]
    public async Task UnscopedRecord_IsCheckedOnAnyScope()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        TestCaller northHolder = await tree.CreateGrantedCallerAsync(tree.North, ManufacturersUpdate);
        TestCaller rootHolder = await tree.CreateGrantedCallerAsync(AuthorizationScope.RootId, ManufacturersUpdate);
        Guid manufacturer = (await tree.SendAsync(new CreateManufacturerCommand(new ManufacturerName($"maker-{Guid.CreateVersion7()}"), null))).Id;

        await using WebApplicationFactory<Program> host = CreateHost();

        // The pipeline passes North's holder at North, and so does the save: the manufacturer is in no scope.
        Assert.False((await SendAsync(host, new RenameAnyManufacturerCommand(manufacturer, "regional"), northHolder, tree.North)).IsError);
        Assert.Equal("regional", await NameOfAsync(manufacturer));

        Assert.False((await SendAsync(host, new RenameAnyManufacturerCommand(manufacturer, "global"), rootHolder, tree.North)).IsError);
        Assert.Equal("global", await NameOfAsync(manufacturer));

        // No grant anywhere is refused by the pipeline, and nothing is saved.
        ErrorOr<Success> ungranted = await SendAsync(host, new RenameAnyManufacturerCommand(manufacturer, "none"), new TestCaller(), tree.North);
        Assert.Equal(ErrorType.Forbidden, ungranted.FirstError.Type);
        Assert.Equal("global", await NameOfAsync(manufacturer));

        // The relay identity, a system identity, passes every check.
        Assert.False((await SendAsync(host, new RenameAnyManufacturerCommand(manufacturer, "relay"), new TestSystemCaller(), tree.North)).IsError);
    }

    [Fact]
    public async Task NewScopedRecord_IsCheckedOnItsStampedScope_UnderAnUnscopedRequest()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        TestCaller northHolder = await tree.CreateGrantedCallerAsync(tree.North, ManufacturersUpdate);
        DeviceModelInternalId model = await CreateModelAsync();

        await using WebApplicationFactory<Program> host = CreateHost();

        // The request is bound to Manufacturer, so the pipeline passes North's holder in South; the device it creates
        // is stamped South, where the holder has nothing, and the save refuses it.
        DeviceId inSouth = new($"device-{Guid.CreateVersion7()}");
        ErrorOr<Success> refused = await SendAsync(host, new CreateDeviceUnderManufacturerCommand(model, inSouth), northHolder, tree.South);
        Assert.Equal(ErrorType.Forbidden, refused.FirstError.Type);
        Assert.False(await DeviceExistsAsync(inSouth));

        DeviceId inNorth = new($"device-{Guid.CreateVersion7()}");
        Assert.False((await SendAsync(host, new CreateDeviceUnderManufacturerCommand(model, inNorth), northHolder, tree.North)).IsError);
        Assert.True(await DeviceExistsAsync(inNorth));
    }

    private WebApplicationFactory<Program> CreateHost() =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(CommandConfigurationSource.FromTypes(
                typeof(RenameAnyManufacturerCommandConfiguration), typeof(CreateDeviceUnderManufacturerCommandConfiguration)));
            services.AddTransient<IRequestHandler<RenameAnyManufacturerCommand, ErrorOr<Success>>, RenameAnyManufacturerHandler>();
            services.AddTransient<IRequestHandler<CreateDeviceUnderManufacturerCommand, ErrorOr<Success>>, CreateDeviceUnderManufacturerHandler>();
        }));

    private static async Task<ErrorOr<Success>> SendAsync(
        WebApplicationFactory<Program> host, ICommand<Success> command, IUserContext caller, Guid scopeId)
    {
        using IServiceScope circuit = host.Services.CreateScope();
        circuit.ServiceProvider.GetRequiredService<UserContextAccessor>().Current = caller;
        circuit.ServiceProvider.GetRequiredService<IAuthorizationScopeContext>().Set(scopeId);

        return await circuit.ServiceProvider.GetRequiredService<ISerginDispatcher>().SendAsync(command);
    }

    private async Task<string> NameOfAsync(Guid manufacturerId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IDeviceManagementDbContext context = scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>();
        ManufacturerId id = new(manufacturerId);

        return (await context.Set<Manufacturer>().AsNoTracking().SingleAsync(manufacturer => manufacturer.Id == id)).Name.Value;
    }

    // Past the scope filter, which is closed outside a request: the question is whether the row was written at all.
    private async Task<bool> DeviceExistsAsync(DeviceId deviceId)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        IDeviceManagementDbContext context = scope.ServiceProvider.GetRequiredService<IDeviceManagementDbContext>();

        return await context.Set<Device>().IgnoreQueryFilters().AnyAsync(device => device.DeviceId == deviceId);
    }

    private async Task<DeviceModelInternalId> CreateModelAsync()
    {
        using IServiceScope scope = factory.Services.CreateScope();
        ISerginDispatcher dispatcher = scope.ServiceProvider.GetRequiredService<ISerginDispatcher>();

        ErrorOr<CreateManufacturerCommandResponse> manufacturer = await dispatcher.SendAsync(
            new CreateManufacturerCommand(new ManufacturerName($"maker-{Guid.CreateVersion7()}"), Address: null));
        ErrorOr<Versioned<ManufacturerQueryResponse>> loaded =
            await dispatcher.SendVersionedAsync(new GetManufacturerByIdQueryCommand(manufacturer.Value.Id));
        ErrorOr<Versioned<AddDeviceModelCommandResponse>> added = await dispatcher.SendVersionedAsync(
            new AddDeviceModelCommand(new ManufacturerId(manufacturer.Value.Id), new DeviceModelName($"model-{Guid.CreateVersion7()}")),
            loaded.Value.Version);
        Assert.False(added.IsError, added.IsError ? added.FirstError.Description : string.Empty);

        return new DeviceModelInternalId(added.Value.Value.Id);
    }

    internal sealed record RenameAnyManufacturerCommand(Guid Id, string Name) : ICommand<Success>;

    // Unbound, so the pipeline checks the current scope; the manufacturer's save asks nothing more.
    internal sealed class RenameAnyManufacturerCommandConfiguration : ICommandConfiguration<RenameAnyManufacturerCommand>
    {
        public void Configure(CommandConfigurationBuilder<RenameAnyManufacturerCommand> builder) =>
            builder.RequirePermissions(ManufacturerPermissions.Update);
    }

    internal sealed class RenameAnyManufacturerHandler(IManufacturerRepository manufacturers, IDeviceManagementUnitOfWork unitOfWork)
        : IRequestHandler<RenameAnyManufacturerCommand, ErrorOr<Success>>
    {
        public async Task<ErrorOr<Success>> Handle(RenameAnyManufacturerCommand request, CancellationToken cancellationToken)
        {
            Manufacturer manufacturer = (await manufacturers.GetAsync(new ManufacturerId(request.Id), cancellationToken))!;
            manufacturer.Update(new ManufacturerName(request.Name), manufacturer.Address);
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success;
        }
    }

    internal sealed record CreateDeviceUnderManufacturerCommand(DeviceModelInternalId ModelId, DeviceId DeviceId) : ICommand<Success>;

    // Bound to Manufacturer, which is not scoped, so the pipeline asks only that the permission is held somewhere.
    internal sealed class CreateDeviceUnderManufacturerCommandConfiguration
        : ICommandConfiguration<CreateDeviceUnderManufacturerCommand, Manufacturer>
    {
        public void Configure(CommandConfigurationBuilder<CreateDeviceUnderManufacturerCommand> builder) =>
            builder.RequirePermissions(ManufacturerPermissions.Update);
    }

    internal sealed class CreateDeviceUnderManufacturerHandler(IDeviceRepository devices, IDeviceManagementUnitOfWork unitOfWork)
        : IRequestHandler<CreateDeviceUnderManufacturerCommand, ErrorOr<Success>>
    {
        public async Task<ErrorOr<Success>> Handle(CreateDeviceUnderManufacturerCommand request, CancellationToken cancellationToken)
        {
            devices.Insert(Device.Create(request.DeviceId, request.ModelId));
            await unitOfWork.SaveChangesAsync(cancellationToken);

            return Result.Success;
        }
    }
}
