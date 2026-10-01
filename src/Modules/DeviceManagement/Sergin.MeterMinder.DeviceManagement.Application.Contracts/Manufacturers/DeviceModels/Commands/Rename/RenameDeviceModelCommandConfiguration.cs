using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Rename;

// Guarded by the manufacturer's version and permission: renaming a model changes the Manufacturer aggregate.
internal sealed class RenameDeviceModelCommandConfiguration : ICommandConfiguration<RenameDeviceModelCommand>
{
    public void Configure(CommandConfigurationBuilder<RenameDeviceModelCommand> builder) =>
        builder.RequirePermissions("permission.dm.manufacturers.update").RequireExpectedVersion();
}
