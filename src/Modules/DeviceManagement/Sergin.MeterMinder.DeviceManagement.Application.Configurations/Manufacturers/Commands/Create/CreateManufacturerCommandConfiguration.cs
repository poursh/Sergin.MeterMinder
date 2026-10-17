using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Create;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.Commands.Create;

// The catalog is global data, so the permission is checked at the root.
internal sealed class CreateManufacturerCommandConfiguration : ICommandConfiguration<CreateManufacturerCommand>
{
    public void Configure(CommandConfigurationBuilder<CreateManufacturerCommand> builder) =>
        builder.RequirePermissions("permission.dm.manufacturers.update").AtRoot();
}
