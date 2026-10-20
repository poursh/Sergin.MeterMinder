using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetList;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.DeviceModels.Commands.GetList;

internal sealed class GetDeviceModelListQueryCommandConfiguration : ICommandConfiguration<GetDeviceModelListQueryCommand, Manufacturer>
{
    public void Configure(CommandConfigurationBuilder<GetDeviceModelListQueryCommand> builder) =>
        builder.RequirePermissions(ManufacturerPermissions.Read);
}
