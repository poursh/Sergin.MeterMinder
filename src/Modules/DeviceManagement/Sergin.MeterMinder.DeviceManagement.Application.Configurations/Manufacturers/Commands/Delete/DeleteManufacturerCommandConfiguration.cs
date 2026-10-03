using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Delete;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.Commands.Delete;

internal sealed class DeleteManufacturerCommandConfiguration : ICommandConfiguration<DeleteManufacturerCommand>
{
    public void Configure(CommandConfigurationBuilder<DeleteManufacturerCommand> builder) =>
        builder.RequirePermissions("permission.dm.manufacturers.delete").RequireExpectedVersion();
}
