using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.ReadProfile;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices.Commands.ReadProfile;

internal sealed class ReadDeviceProfileCommandConfiguration : ICommandConfiguration<ReadDeviceProfileCommand>
{
    public void Configure(CommandConfigurationBuilder<ReadDeviceProfileCommand> builder) =>
        builder.RequirePermissions(DevicePermissions.ReadProfile)
            .Task(task => task
                .InactivityTimeout(TimeSpan.FromMinutes(2))
                .Schedulable());
}
