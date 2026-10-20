using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.Delete;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices.Commands.Delete;

internal sealed class DeleteDeviceCommandConfiguration : ICommandConfiguration<DeleteDeviceCommand, Device>
{
    public void Configure(CommandConfigurationBuilder<DeleteDeviceCommand> builder) =>
        builder.RequirePermissions(DevicePermissions.Delete).RequireExpectedVersion();
}
