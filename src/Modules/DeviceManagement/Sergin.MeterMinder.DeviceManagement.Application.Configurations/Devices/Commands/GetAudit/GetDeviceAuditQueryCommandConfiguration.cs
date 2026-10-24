using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.GetAudit;
using Sergin.MeterMinder.DeviceManagement.Domain.Devices;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices.Commands.GetAudit;

internal sealed class GetDeviceAuditQueryCommandConfiguration : ICommandConfiguration<GetDeviceAuditQueryCommand, Device>
{
    public void Configure(CommandConfigurationBuilder<GetDeviceAuditQueryCommand> builder) =>
        builder.RequirePermissions(DevicePermissions.Read);
}
