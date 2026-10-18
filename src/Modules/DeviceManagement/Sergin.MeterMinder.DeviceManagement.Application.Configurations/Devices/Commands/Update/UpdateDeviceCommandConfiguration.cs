using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Update;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices.Commands.Update;

internal sealed class UpdateDeviceCommandConfiguration : ICommandConfiguration<UpdateDeviceCommand, Device>
{
    public void Configure(CommandConfigurationBuilder<UpdateDeviceCommand> builder) =>
        builder.RequirePermissions("permission.dm.devices.update").RequireExpectedVersion();
}
