using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Remove;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.DeviceModels.Commands.Remove;

// Guarded by the manufacturer's version (from the record's type) and permission: removing a model changes the
// Manufacturer aggregate.
internal sealed class RemoveDeviceModelCommandConfiguration : ICommandConfiguration<RemoveDeviceModelCommand>
{
    public void Configure(CommandConfigurationBuilder<RemoveDeviceModelCommand> builder) =>
        builder.RequirePermissions(ManufacturerPermissions.Update)
            .Texts(DeviceModelTexts.Category);
}
