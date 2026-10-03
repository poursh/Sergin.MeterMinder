using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.GetOne;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.DeviceModels.Commands.GetOne;

internal sealed class GetDeviceModelByIdQueryCommandConfiguration : ICommandConfiguration<GetDeviceModelByIdQueryCommand>
{
    public void Configure(CommandConfigurationBuilder<GetDeviceModelByIdQueryCommand> builder) =>
        builder.RequirePermissions("permission.dm.manufacturers.read");
}
