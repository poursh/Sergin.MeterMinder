using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Delete;

internal sealed class DeleteDeviceCommandConfiguration : ICommandConfiguration<DeleteDeviceCommand>
{
    public void Configure(CommandConfigurationBuilder<DeleteDeviceCommand> builder) =>
        builder.RequirePermissions("permission.dm.devices.delete").RequireExpectedVersion();
}
