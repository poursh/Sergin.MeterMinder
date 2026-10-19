using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetOne;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices.Commands.GetOne;

internal sealed class GetDeviceByIdQueryCommandConfiguration : ICommandConfiguration<GetDeviceByIdQueryCommand, Device>
{
    public void Configure(CommandConfigurationBuilder<GetDeviceByIdQueryCommand> builder) =>
        builder.RequirePermissions("permission.dm.devices.read");
}
