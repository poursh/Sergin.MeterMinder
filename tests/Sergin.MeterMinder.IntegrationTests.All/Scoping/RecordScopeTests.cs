using ErrorOr;
using MediatR;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Sergin.MeterMinder.DeviceManagement.Application;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;
using Sergin.MeterMinder.IntegrationTests.All.Authorization;
using Sergin.SharedKernel.Application.Commands;
using Sergin.SharedKernel.Application.Commands.Configuration;
using Sergin.SharedKernel.Application.Securities.Authorization;
using Sergin.SharedKernel.Application.Securities.Users;
using Sergin.SharedKernel.Domain.Securities;
using Sergin.SharedKernel.IntegrationTests;
using Sergin.SharedKernel.Presentation.Blazor.Dispatching;

namespace Sergin.MeterMinder.IntegrationTests.All.Scoping;

/// <summary>
/// RecordScopeInterceptor: a save is checked again on the scope of every record it writes. A manufacturer is global
/// data, so it lives at the root: a command that forgets AtRoot() still cannot let a regional holder change it.
/// </summary>
[Collection(nameof(IntegrationTestCollection))]
public sealed class RecordScopeTests(SerginWebApiFactory<Program> factory)
{
    private static readonly Permission ManufacturersUpdate = "permission.dm.manufacturers.update";

    [Fact]
    public async Task UnscopedRecord_IsCheckedAtTheRoot_EvenWithoutAtRoot()
    {
        RbacScenario tree = await RbacScenario.BuildAsync(factory);
        TestCaller northHolder = await tree.CreateGrantedCallerAsync(tree.North, ManufacturersUpdate);
        TestCaller rootHolder = await tree.CreateGrantedCallerAsync(AuthorizationScope.RootId, ManufacturersUpdate);
        Guid manufacturer = (await tree.SendAsync(new CreateManufacturerCommand(new ManufacturerName($"maker-{Guid.CreateVersion7()}"), null))).Id;

        await using WebApplicationFactory<Program> host = CreateHost();

        // The pipeline passes North's holder at North; the save refuses, since the manufacturer is not North's.
        ErrorOr<Success> regional = await SendAsync(host, new RenameAnyManufacturerCommand(manufacturer, "regional"), northHolder, tree.North);
        Assert.Equal(ErrorType.Forbidden, regional.FirstError.Type);
        Assert.NotEqual("regional", await NameOfAsync(manufacturer));

        Assert.False((await SendAsync(host, new RenameAnyManufacturerCommand(manufacturer, "global"), rootHolder, tree.North)).IsError);
        Assert.Equal("global", await NameOfAsync(manufacturer));

        // The relay identity, a system admin, passes every check.
        Assert.False((await SendAsync(host, new RenameAnyManufacturerCommand(manufacturer, "relay"), new TestCaller { IsSystemAdmin = true }, tree.North)).IsError);
    }

    private WebApplicationFactory<Program> CreateHost() =>
        factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton(CommandConfigurationSource.FromTypes(typeof(RenameAnyManufacturerCommandConfiguration)));
            services.AddTransient<IRequestHandler<RenameAnyManufacturerCommand, ErrorOr<Success>>, RenameAnyManufacturerHandler>();
        }));

    private static async Task<ErrorOr<Success>> SendAsync(
        WebApplicationFactory<Program> host, RenameAnyManufacturerCommand command, IUserContext caller, Guid scopeId)
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

    internal sealed record RenameAnyManufacturerCommand(Guid Id, string Name) : ICommand<Success>;

    // Deliberately without AtRoot(): the interceptor is what holds.
    internal sealed class RenameAnyManufacturerCommandConfiguration : ICommandConfiguration<RenameAnyManufacturerCommand>
    {
        public void Configure(CommandConfigurationBuilder<RenameAnyManufacturerCommand> builder) =>
            builder.RequirePermissions(ManufacturersUpdate);
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
}
