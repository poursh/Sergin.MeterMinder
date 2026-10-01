using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetList;

internal sealed class GetManufacturerListQueryCommandConfiguration : ICommandConfiguration<GetManufacturerListQueryCommand>
{
    public void Configure(CommandConfigurationBuilder<GetManufacturerListQueryCommand> builder) =>
        builder.RequirePermissions("permission.dm.manufacturers.read");
}
