using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Devices.Commands.ReadProfile;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Devices.Commands.ReadProfile;

internal sealed class ReadDeviceProfileCommandConfiguration : ICommandConfiguration<ReadDeviceProfileCommand>
{
    // A version stored with a schedule would be stale by the next run, so the command waives it.
    public void Configure(CommandConfigurationBuilder<ReadDeviceProfileCommand> builder) =>
        builder.Schedulable("dm.devices.read-profile")
            .RequirePermissions(DevicePermissions.ReadProfile)
            .WithoutExpectedVersion();
}
