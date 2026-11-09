using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Rename;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.DeviceModels.Commands.Rename;

// Guarded by the manufacturer's version (from the record's type) and permission: renaming a model changes the
// Manufacturer aggregate.
internal sealed class RenameDeviceModelCommandConfiguration : ICommandConfiguration<RenameDeviceModelCommand>
{
    public void Configure(CommandConfigurationBuilder<RenameDeviceModelCommand> builder) =>
        builder.RequirePermissions(ManufacturerPermissions.Update)
            .Texts(DeviceModelTexts.Category);
}
