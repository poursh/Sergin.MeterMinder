using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.DeviceModels.Commands.Remove;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.DeviceModels.Commands.Remove;

// Guarded by the manufacturer's version and permission: removing a model changes the Manufacturer aggregate.
internal sealed class RemoveDeviceModelCommandConfiguration : ICommandConfiguration<RemoveDeviceModelCommand, Manufacturer>
{
    public void Configure(CommandConfigurationBuilder<RemoveDeviceModelCommand> builder) =>
        builder.RequirePermissions(ManufacturerPermissions.Update).RequireExpectedVersion();
}
