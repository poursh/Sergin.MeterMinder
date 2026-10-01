using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetOne;

internal sealed class GetManufacturerByIdQueryCommandConfiguration : ICommandConfiguration<GetManufacturerByIdQueryCommand>
{
    public void Configure(CommandConfigurationBuilder<GetManufacturerByIdQueryCommand> builder) =>
        builder.RequirePermissions("permission.dm.manufacturers.read");
}
