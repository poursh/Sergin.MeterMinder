using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetOverview;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices.Commands.GetOverview;

internal sealed class GetDeviceOverviewQueryCommandConfiguration : ICommandConfiguration<GetDeviceOverviewQueryCommand>
{
    public void Configure(CommandConfigurationBuilder<GetDeviceOverviewQueryCommand> builder) =>
        builder.RequirePermissions(DevicePermissions.Read);
}
