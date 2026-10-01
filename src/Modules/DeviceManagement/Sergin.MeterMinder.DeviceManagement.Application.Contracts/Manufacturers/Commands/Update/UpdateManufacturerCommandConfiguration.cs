using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Update;

internal sealed class UpdateManufacturerCommandConfiguration : ICommandConfiguration<UpdateManufacturerCommand>
{
    public void Configure(CommandConfigurationBuilder<UpdateManufacturerCommand> builder) =>
        builder.RequirePermissions("permission.dm.manufacturers.update").RequireExpectedVersion();
}
