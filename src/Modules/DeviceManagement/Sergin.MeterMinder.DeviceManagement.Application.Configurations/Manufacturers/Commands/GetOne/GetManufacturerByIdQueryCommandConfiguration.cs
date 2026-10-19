using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.Commands.GetOne;

internal sealed class GetManufacturerByIdQueryCommandConfiguration : ICommandConfiguration<GetManufacturerByIdQueryCommand, Manufacturer>
{
    public void Configure(CommandConfigurationBuilder<GetManufacturerByIdQueryCommand> builder) =>
        builder.RequirePermissions("permission.dm.manufacturers.read");
}
