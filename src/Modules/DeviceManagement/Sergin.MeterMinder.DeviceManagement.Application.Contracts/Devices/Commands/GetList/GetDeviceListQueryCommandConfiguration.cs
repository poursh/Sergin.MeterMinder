using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetList;

internal sealed class GetDeviceListQueryCommandConfiguration : ICommandConfiguration<GetDeviceListQueryCommand>
{
    public void Configure(CommandConfigurationBuilder<GetDeviceListQueryCommand> builder) =>
        builder.RequirePermissions("permission.dm.devices.read");
}
