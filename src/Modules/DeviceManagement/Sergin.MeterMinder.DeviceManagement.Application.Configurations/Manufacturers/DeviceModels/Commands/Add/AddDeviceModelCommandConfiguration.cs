using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Add;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.DeviceModels.Commands.Add;

// Guarded by the manufacturer's version, which the record's type implies: adding a model changes the Manufacturer
// aggregate. The catalog is global data: Manufacturer is not scoped, so the permission is held on any scope.
internal sealed class AddDeviceModelCommandConfiguration : ICommandConfiguration<AddDeviceModelCommand>
{
    public void Configure(CommandConfigurationBuilder<AddDeviceModelCommand> builder) =>
        builder.RequirePermissions(ManufacturerPermissions.Update)
            .Texts(DeviceModelTexts.Category);
}
