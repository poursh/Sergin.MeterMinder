using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Create;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.Commands.Create;

// The catalog is global data: its root, Manufacturer, is not scoped, so the permission is held on any scope.
internal sealed class CreateManufacturerCommandConfiguration : ICommandConfiguration<CreateManufacturerCommand>
{
    public void Configure(CommandConfigurationBuilder<CreateManufacturerCommand> builder) =>
        builder.RequirePermissions(ManufacturerPermissions.Update);
}
