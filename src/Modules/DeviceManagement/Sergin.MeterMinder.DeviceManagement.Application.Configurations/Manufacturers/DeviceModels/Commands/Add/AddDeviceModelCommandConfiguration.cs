using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Add;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.DeviceModels.Commands.Add;

// Guarded by the manufacturer's version: adding a model changes the Manufacturer aggregate. The catalog is global
// data, so the permission is checked at the root.
internal sealed class AddDeviceModelCommandConfiguration : ICommandConfiguration<AddDeviceModelCommand, Manufacturer>
{
    public void Configure(CommandConfigurationBuilder<AddDeviceModelCommand> builder) =>
        builder.RequirePermissions("permission.dm.manufacturers.update").RequireExpectedVersion();
}
