using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetOne;

internal sealed class GetDeviceByIdQueryCommandConfiguration : ICommandConfiguration<GetDeviceByIdQueryCommand>
{
    public void Configure(CommandConfigurationBuilder<GetDeviceByIdQueryCommand> builder) =>
        builder.RequirePermissions("permission.dm.devices.read");
}
