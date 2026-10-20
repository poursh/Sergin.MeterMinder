using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers;
using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.Update;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.Commands.Update;

internal sealed class UpdateManufacturerCommandConfiguration : ICommandConfiguration<UpdateManufacturerCommand, Manufacturer>
{
    public void Configure(CommandConfigurationBuilder<UpdateManufacturerCommand> builder) =>
        builder.RequirePermissions(ManufacturerPermissions.Update).RequireExpectedVersion();
}
