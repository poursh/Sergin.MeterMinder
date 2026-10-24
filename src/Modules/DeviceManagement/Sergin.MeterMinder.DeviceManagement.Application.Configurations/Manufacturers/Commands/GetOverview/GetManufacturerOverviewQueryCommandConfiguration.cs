using Sergin.MeterMinder.DeviceManagement.Application.Contracts.Manufacturers.Commands.GetOverview;
using Sergin.MeterMinder.DeviceManagement.Domain.Manufacturers;
using Sergin.SharedKernel.Application.Commands.Configuration;

namespace Sergin.MeterMinder.DeviceManagement.Application.Configurations.Manufacturers.Commands.GetOverview;

internal sealed class GetManufacturerOverviewQueryCommandConfiguration : ICommandConfiguration<GetManufacturerOverviewQueryCommand, Manufacturer>
{
    public void Configure(CommandConfigurationBuilder<GetManufacturerOverviewQueryCommand> builder) =>
        builder.RequirePermissions(ManufacturerPermissions.Read);
}
