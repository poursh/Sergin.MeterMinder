using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Create;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices.Commands.Create;

// A new device is stamped with the caller's current scope, so creating one needs the right to change devices there.
internal sealed class CreateDeviceCommandConfiguration : ICommandConfiguration<CreateDeviceCommand>
{
    public void Configure(CommandConfigurationBuilder<CreateDeviceCommand> builder) =>
        builder.RequirePermissions("permission.dm.devices.update");
}
